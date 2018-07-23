namespace BigramHistogram
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipelines;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class BigramHistogram
    {
        /// <summary>
        /// Entry point for the application. Figures out whether to use stdin or open files, gets the appropriate
        /// streams, processes them in parallel, and then writes the final results (or errors) to the console.
        /// </summary>
        /// <returns>
        /// <c>0</c> if the program was successful, <c>1</c> if there was an error getting the input, or <c>2</c> if
        /// there was an error processing the text.
        /// </returns>
        public static async Task<int> Main(string[] args)
        {
            var streams = new List<Stream>();

            if (args.Length == 0) // no files to open, so use stdin instead
            {
                streams.Add(Console.OpenStandardInput());
            }
            else
            {
                foreach (var fileName in args)
                {
                    try
                    {
                        streams.Add(File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                        // not disposing the FileStream because this is a short-lived program and FileShare.ReadWrite
                        // allows other processes to continue working with the file
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Error opening file \"{0}\":", fileName);
                        WriteExceptionsToConsole(ex);

                        return 1;
                    }
                }
            }

            var histogram = new ConcurrentDictionary<(string, string), int>();

            try
            {
                await Task.WhenAll(
                    streams.Select(stream => ProcessStream(histogram, stream)).ToArray()
                );
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error processing text:");
                WriteExceptionsToConsole(e);

                return 2;
            }

            foreach (var pair in histogram)
            {
                Console.WriteLine("\"{0} {1}\": {2}", pair.Key.Item1, pair.Key.Item2, pair.Value);
            }

            return 0;
        }

        /// <summary>
        /// Writes exception details to the console using stderr. Recursively handles inner exceptions, and has a
        /// special case for aggregate exceptions (to write the entire collection of inner exceptions).
        /// </summary>
        private static void WriteExceptionsToConsole(Exception exception)
        {
            if (exception == null) return;

            Console.Error.WriteLine();
            Console.Error.WriteLine(exception.ToString());

            if (exception is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    WriteExceptionsToConsole(innerException);
                }
            }
            else
            {
                WriteExceptionsToConsole(exception.InnerException);
            }
        }

        /// <summary>
        /// Reads a stream of text and counts its bigrams using a <see cref="Pipe" />.
        /// </summary>
        /// <remarks>
        /// The code in this function and the functions it calls are based on the <see cref="System.IO.Pipelines" />
        /// and <see cref="System.Buffers" /> namespaces that make up the new Pipelines API in .NET Core. For more
        /// information, see
        /// https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/
        /// and
        /// https://blogs.msdn.microsoft.com/mazhou/2018/03/25/c-7-series-part-10-spant-and-universal-memory-management/
        /// </remarks>
        public static async Task ProcessStream(ConcurrentDictionary<(string, string), int> histogram, Stream stream)
        {
            var pipe = new Pipe();

            var writerTask = WriteStreamToPipeAsync(stream, pipe.Writer);
            var readerTask = ReadPipeAndProcessAsync(histogram, pipe.Reader);

            await Task.WhenAll(readerTask, writerTask);
        }

        /// <summary>
        /// Asynchronously writes the entire contents of a <see cref="Stream" /> to a <see cref="PipeWriter" />.
        /// </summary>
        private static async Task WriteStreamToPipeAsync(Stream stream, PipeWriter writer)
        {
            try
            {
                while (true)
                {
                    var memory = writer.GetMemory();

                    var bytesRead = await stream.ReadAsync(memory);
                    if (bytesRead == 0) // stream finished
                    {
                        break;
                    }

                    writer.Advance(bytesRead);

                    var result = await writer.FlushAsync();
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                writer.Complete(ex);
                throw;
            }

            writer.Complete();
        }

        /// <summary>
        /// Asynchronously reads the contents of a <see cref="PipeReader" />, extracts the bigrams, and adds their
        /// occurrences to the histogram.
        /// </summary>
        private static async Task ReadPipeAndProcessAsync(
            ConcurrentDictionary<(string, string), int> histogram, PipeReader reader
        )
        {
            string currentFirstWord = null;

            var currentlyInWord = false;
            var possibleInWordPunctuation = false;

            try
            {
                while (true)
                {
                    var result = await reader.ReadAsync();
                    var buffer = result.Buffer;

                    var consumedPosition = ProcessBuffer(
                        buffer, histogram, ref currentFirstWord, ref currentlyInWord, ref possibleInWordPunctuation,
                        result.IsCompleted
                    );

                    reader.AdvanceTo(consumedPosition, buffer.End);
                    if (result.IsCompleted)
                    {
                        reader.Complete();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                reader.Complete(ex);
                throw;
            }
        }

        /// <summary>
        /// Given a buffer and the current state, examines and consumes the contents of the buffers and updates the
        /// state (and histogram) when individual words are identified.
        /// </summary>
        /// <returns>The <see cref="SequencePosition" /> of the last consumed byte in the buffer.</returns>
        private static SequencePosition ProcessBuffer(
            ReadOnlySequence<byte> buffer, ConcurrentDictionary<(string, string), int> histogram,
            ref string currentFirstWord, ref bool currentlyInWord, ref bool possibleInWordPunctuation, bool isCompleted
        )
        {
            var position = buffer.Start;
            var consumedPosition = position;

            foreach (var segment in buffer)
            {
                foreach (var b in segment.Span)
                {
                    var c = (char)b;

                    if (char.IsLetter(c))
                    { // inside or beginning a word, with an actual letter characters
                        currentlyInWord = true;
                        possibleInWordPunctuation = false;
                    }
                    else if (!possibleInWordPunctuation && currentlyInWord && (c == '-' || c == '\''))
                    { // encountered a hyphen or an apostraphe, could be in-word punctuation
                        possibleInWordPunctuation = true;
                    }
                    else
                    { // encountered a non-word character, so if we were previously in a word, add it to the histogram
                        if (currentlyInWord)
                        {
                            var wordBuffer = buffer.Slice(buffer.Start, position);

                            UpdateStateAndAddEntry(
                                histogram, ref currentFirstWord, wordBuffer, out currentlyInWord,
                                ref possibleInWordPunctuation
                            );
                        }

                        consumedPosition = buffer.GetPosition(1, position);
                        buffer = buffer.Slice(consumedPosition);
                    }

                    position = buffer.GetPosition(1, position);
                }
            }

            if (currentlyInWord && isCompleted)
            { // if the pipe completes while still in a word, make sure it gets added to the histogram
                UpdateStateAndAddEntry(
                    histogram, ref currentFirstWord, buffer, out currentlyInWord, ref possibleInWordPunctuation
                );

                return buffer.End;
            }

            return consumedPosition;
        }

        /// <summary>
        /// Given the current state, add an occurrence to the histogram (if appropriate) and prepare the state for the
        /// next word occurrence.
        /// </summary>
        private static void UpdateStateAndAddEntry(
            ConcurrentDictionary<(string, string), int> histogram, ref string currentFirstWord,
            ReadOnlySequence<byte> secondWordBuffer, out bool currentlyInWord, ref bool possibleInWordPunctuation
        )
        {
            if (possibleInWordPunctuation)
            { // if we got here, the word ended with a hyphen or apostraphe, so treat it as a non-word character
                secondWordBuffer = secondWordBuffer.Slice(0, secondWordBuffer.Length - 1);
                possibleInWordPunctuation = false;
            }

            // copy the word buffer to an array and make a string out of it
            var secondWordBytes = new byte[secondWordBuffer.Length];
            secondWordBuffer.CopyTo(secondWordBytes);

            var currentSecondWord = Encoding.ASCII.GetString(secondWordBytes).ToLowerInvariant();

            if (currentFirstWord != null)
            { // if there's no current first word, then this is the very first word, so don't try to make a bigram
                histogram.AddOrUpdate((currentFirstWord, currentSecondWord), 1, (_, count) => count + 1);
            }

            // swap the most recent word into the first word slot and update the state
            currentFirstWord = currentSecondWord;
            currentlyInWord = false;
        }
    }
}
