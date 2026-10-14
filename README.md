[GitHub Releases](https://github.com/p0lymeric/TMfDSBetterSignalCompiler/releases)

# Better Signal Compiler

The year is 1975. Dr. Bautista recently attended a [seminar on algorithm design](https://en.wikipedia.org/wiki/Aho%E2%80%93Corasick_algorithm#History)[1], in which he experienced a mildly disturbing callback to one of his prior projects. "These techniques would have been useful for handling the *Transmission*...", he thought, vexed at himself. "But it is not too late..."

Better Signal Compiler replaces the signal compiler in *The Message from Deep Space* with a better behaved implementation.

It is the author's hope that the replacement's behaviour is more predictable, and better suited for editing transmissions using larger dictionaries (that are more likely to bump into limitations of the original signal compiler).

## Installation

Better Signal Compiler depends on BepInEx v5 as a mod loader.

1. Download [BepInEx v5](https://github.com/BepInEx/BepInEx/releases) and extract it to the game root directory.
2. Download [Better Signal Compiler](https://github.com/p0lymeric/TMfDSBetterSignalCompiler/releases) and merge it with the BepInEx directory.
3. After running the game once, you can inspect whether Better Signal Compiler loaded successfully by checking BepInEx/LogOutput.log (`[Info   :Better Signal Compiler] Hello from polymeric.BetterSignalCompiler vx.x.x!`)
4. After running the game once, a configuration file will be generated under `BepInEx/config/polymeric.BetterSignalCompiler.cfg`. This file may be directly edited, or another mod like [BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager) can be used to edit the configuration in-game.

## Features
### Compiler

Like the game's compiler, Better Signal Compiler is capable of inferring token boundaries where whitespace is not present, but distinctively, it also uses whitespace to enforce separation between two tokens.

- Consider a dictionary with three words: CAN, DO, and AND. The original signal compiler struggles with parsing the phrase "CAN DO", as it internally removes whitespace from the string, and due to a greedy longer-word first scan, can either recognize "AND" or "CAN" first within the substring, which leads to unstable behaviour on whether the string parses as ["C", "AND", "O"] or ["CAN", "DO"].

Better Signal Compiler considers all possible tokenizations and will find solutions that fully cover non-whitespace in a string, if any were to exist.
- With the same dictionary, it stably and unambiguously tokenizes "CAN DO" as ["CAN", "DO"].

Better Signal Compiler knows if an input string can be uniquely tokenized and rejects a parse if not so.
- For example, consider a dictionary with words: "X", "XX", "XOR", and "OR". The string "XXORX" could either be parsed as ["X", "XOR", "X"] or ["XX", "OR", "X"], and is rejected as ambiguous, whereas "X XOR X" or "XX OR X" are unambiguous.

Better Signal Compiler handles other boundary cases, like if a dictionary were to contain both compound and root words.
- Consider a dictionary with three words: FIRE, TRUCK, and FIRETRUCK. Better Signal Compiler prefers to minimize token counts, which leads to "FIRETRUCK" unambiguously parsing as ["FIRETRUCK"], and "FIRE TRUCK" parsing as ["FIRE", "TRUCK"].

### Syntax extensions

Better Signal Compiler can accept raw signal IDs in the form `|<integer>` (e.g. `|5`, `|-244`), as an alternative to dictionary words or digits. This parsing is enabled by default, and may be disabled by setting `[Parser].ParseBareIds = false` in mod settings.

### Error messages

Better Signal Compiler can override the formatting of errors printed to the console.
- A formatting style similar to the original game's format is selected by default (`[ErrorLog].DisplayFormat = Verbose`).
- An alternative is available (`[ErrorLog].DisplayFormat = TraceAndCount/TraceAndVerbose`), which prints a trace of parsed raw signal IDs interspersed with unconsumed portions of the string in the error summary.
- For example, consider a dictionary with four words: HA(-299), WHOOPS(-300), HERE(-301), and TY(-302). `HAHA WHOOPS TYPO HERE` generates a trace of `|-299 |-299 |-300 |-302 PO |-301`.
- When a trace format is selected, raw signal IDs may be further collapsed by setting `[ErrorLog].AbbreviateTrace = true`. If that were set, the above example would generate a trace of `|... PO |...`.

(The author thinks that `[ErrorLog].DisplayFormat = TraceAndCount` and `[ErrorLog].AbbreviateTrace = true` make the most of the limited space on the game's console display, for the purpose of highlighting typos.)

## Issue reporting
Please [file an issue on GitHub](https://github.com/p0lymeric/TMfDSBetterSignalCompiler/issues).

## Licensing
This mod is released under the terms of the MIT license.

## Footnotes
[1] This mod does not actually use Aho-Corasick to collect token positions, and instead uses a simpler implementation by repeatedly iterating over a plain prefix trie. I just thought the coincidence in timeline was fun and that Corasick's story was an interesting demonstration of the limited state of the art at the time when Bautista would've conceived the compiler used in *TMfDS*.
