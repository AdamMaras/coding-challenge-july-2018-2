namespace BigramHistogram.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class BigramTests
    {
        /// <summary>
        /// Makes a stream with ASCII bytes from a string.
        /// </summary>
        private Stream StringStream(string s, Encoding encoding = null)
        {
            var stream = new MemoryStream();

            using (var writer = new StreamWriter(stream, encoding ?? Encoding.ASCII, Math.Max(1, s.Length), true))
            {
                writer.Write(s);
            }

            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

        /// <summary>
        /// Helper to make a blank histogram and run the processing function.
        /// </summary>
        private async Task<ConcurrentDictionary<(string, string), int>> Process(string s)
        {
            var histogram = new ConcurrentDictionary<(string, string), int>();

            using (var stream = StringStream(s))
            {
                await BigramHistogram.ProcessStream(histogram, stream);
                // don't need to dispose MemoryStream, no unmanaged resources
            }

            return histogram;
        }

        private async Task CompareResults(string s, Dictionary<(string, string), int> histogram) =>
            CollectionAssert.AreEquivalent(histogram, await Process(s));

        [TestMethod]
        public async Task ProvidedSample() => await CompareResults(
            "The quick brown fox and the quick blue hare.",
            new Dictionary<(string, string), int>
            {
                [("the", "quick")] = 2,
                [("quick", "brown")] = 1,
                [("brown", "fox")] = 1,
                [("fox", "and")] = 1,
                [("and", "the")] = 1,
                [("quick", "blue")] = 1,
                [("blue", "hare")] = 1
            }
        );

        [TestMethod]
        public async Task EmptyString() => await CompareResults(
            "",
            new Dictionary<(string, string), int>()
        );

        [TestMethod]
        public async Task OneToken() => await CompareResults(
            "token",
            new Dictionary<(string, string), int>()
        );

        [TestMethod]
        public async Task TwoMismatchedTokens() => await CompareResults(
            "tokena tokenb",
            new Dictionary<(string, string), int>
            {
                [("tokena", "tokenb")] = 1
            }
        );

        [TestMethod]
        public async Task TwoMatchingTokens() => await CompareResults(
            "token token",
            new Dictionary<(string, string), int>
            {
                [("token", "token")] = 1
            }
        );

        [TestMethod]
        public async Task ThreeMismatchedTokens() => await CompareResults(
            "tokena tokenb tokenc",
            new Dictionary<(string, string), int>
            {
                [("tokena", "tokenb")] = 1,
                [("tokenb", "tokenc")] = 1
            }
        );

        [TestMethod]
        public async Task ThreeMatchingTokens() => await CompareResults(
            "token token token",
            new Dictionary<(string, string), int>
            {
                [("token", "token")] = 2
            }
        );

        [TestMethod]
        public async Task MismatchedCapitalization() => await CompareResults(
            "TOKEN token",
            new Dictionary<(string, string), int>
            {
                [("token", "token")] = 1
            }
        );

        [TestMethod]
        public async Task MultipleBreakCharacters() => await CompareResults(
            "token   token",
            new Dictionary<(string, string), int>
            {
                [("token", "token")] = 1
            }
        );

        [TestMethod]
        public async Task BreakCharactersAtStart() => await CompareResults(
            "   token token",
            new Dictionary<(string, string), int>
            {
                [("token", "token")] = 1
            }
        );

        [TestMethod]
        public async Task BreakCharactersAtEnd() => await CompareResults(
            "token token   ",
            new Dictionary<(string, string), int>
            {
                [("token", "token")] = 1
            }
        );

        [TestMethod]
        public async Task HyphenatedTokens() => await CompareResults(
            "token-a token-b",
            new Dictionary<(string, string), int>
            {
                [("token-a", "token-b")] = 1
            }
        );

        [TestMethod]
        public async Task NotActuallyHyphenatedTokens() => await CompareResults(
            "-tokena- -tokenb-",
            new Dictionary<(string, string), int>
            {
                [("tokena", "tokenb")] = 1
            }
        );

        [TestMethod]
        public async Task ApostraphedTokens() => await CompareResults(
            "token'a token'b",
            new Dictionary<(string, string), int>
            {
                [("token'a", "token'b")] = 1
            }
        );

        [TestMethod]
        public async Task NotActuallyApostraphedTokens() => await CompareResults(
            "'tokena' 'tokenb'",
            new Dictionary<(string, string), int>
            {
                [("tokena", "tokenb")] = 1
            }
        );

        [TestMethod]
        public async Task Lipsum() => await CompareResults(
            @"Aliquam vitae feugiat lacus. Proin a augue sem. Maecenas scelerisque interdum sem. Morbi vel sodales arcu.
Nam sit amet blandit mi, eu facilisis mi. Phasellus sed varius ante. Nunc tincidunt, nunc id lobortis aliquet, lorem
tortor suscipit mauris, nec ultricies ante purus non enim. Duis ultrices sem eu gravida finibus. Aliquam aliquet odio
eget est mollis, eu auctor tortor tincidunt. Quisque sagittis dapibus massa.",
            new Dictionary<(string, string), int>
            {
                [("dapibus", "massa")] = 1,
                [("a", "augue")] = 1,
                [("tortor", "tincidunt")] = 1,
                [("sem", "maecenas")] = 1,
                [("feugiat", "lacus")] = 1,
                [("id", "lobortis")] = 1,
                [("phasellus", "sed")] = 1,
                [("morbi", "vel")] = 1,
                [("sed", "varius")] = 1,
                [("finibus", "aliquam")] = 1,
                [("est", "mollis")] = 1,
                [("proin", "a")] = 1,
                [("mi", "eu")] = 1,
                [("sodales", "arcu")] = 1,
                [("ultricies", "ante")] = 1,
                [("varius", "ante")] = 1,
                [("amet", "blandit")] = 1,
                [("nunc", "tincidunt")] = 1,
                [("arcu", "nam")] = 1,
                [("ante", "purus")] = 1,
                [("mauris", "nec")] = 1,
                [("nec", "ultricies")] = 1,
                [("eget", "est")] = 1,
                [("auctor", "tortor")] = 1,
                [("nunc", "id")] = 1,
                [("ultrices", "sem")] = 1,
                [("vel", "sodales")] = 1,
                [("enim", "duis")] = 1,
                [("aliquam", "aliquet")] = 1,
                [("lobortis", "aliquet")] = 1,
                [("sem", "morbi")] = 1,
                [("odio", "eget")] = 1,
                [("facilisis", "mi")] = 1,
                [("tincidunt", "nunc")] = 1,
                [("eu", "gravida")] = 1,
                [("eu", "auctor")] = 1,
                [("quisque", "sagittis")] = 1,
                [("vitae", "feugiat")] = 1,
                [("suscipit", "mauris")] = 1,
                [("sem", "eu")] = 1,
                [("tincidunt", "quisque")] = 1,
                [("eu", "facilisis")] = 1,
                [("interdum", "sem")] = 1,
                [("tortor", "suscipit")] = 1,
                [("lacus", "proin")] = 1,
                [("sagittis", "dapibus")] = 1,
                [("gravida", "finibus")] = 1,
                [("mollis", "eu")] = 1,
                [("purus", "non")] = 1,
                [("augue", "sem")] = 1,
                [("lorem", "tortor")] = 1,
                [("aliquet", "odio")] = 1,
                [("maecenas", "scelerisque")] = 1,
                [("aliquam", "vitae")] = 1,
                [("scelerisque", "interdum")] = 1,
                [("aliquet", "lorem")] = 1,
                [("duis", "ultrices")] = 1,
                [("mi", "phasellus")] = 1,
                [("ante", "nunc")] = 1,
                [("nam", "sit")] = 1,
                [("blandit", "mi")] = 1,
                [("sit", "amet")] = 1,
                [("non", "enim")] = 1
            }
        );
    }
}
