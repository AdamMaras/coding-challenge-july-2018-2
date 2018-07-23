# BigramHistogram

Reads ASCII text, extracts individual words, and counts each occurrence of adjacent pairs (bigrams).

## Example

> The quick brown fox and the quick blue hare.

```
"the quick": 2
"quick brown": 1
"brown fox": 1
"fox and": 1
"and the": 1
"quick blue": 1
"blue hare": 1
```

## Try it

```sh
# pipe to stdin
$ echo "Buffalo buffalo Buffalo buffalo buffalo buffalo Buffalo buffalo." | dotnet run --project src/BigramHistogram/
"buffalo buffalo": 7

# provide a file
$ dotnet run --project src/BigramHistogram/ -- myfile.txt
"your results": 1
"results here": 1
```

## Run tests

```sh
dotnet test test/BigramHistogram.Tests/
```
