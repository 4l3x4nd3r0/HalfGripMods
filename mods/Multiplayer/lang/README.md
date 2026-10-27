# Translating Multiplayer

The mod has no translations of its own yet, but it reads them if they exist. It uses the game's own system: the English text is the key, and the game shows the translated text for the language chosen in its settings.

1. Copy `_template.csv` and name the copy after the game's language, in lower case: `french.csv`, `german.csv`, `spanish.csv`, `italian.csv`, `romanian.csv`, `russian.csv`, `turkish.csv`, `brazilian.csv`. Only languages the game itself has can be chosen in its settings.
2. Fill the second column. Leave a row empty to keep the English text. Do not change the first column: it has to match the text on screen exactly.
3. Keep `{0}` and `{1}` where they are. The game puts a name or a number there (`PLAYERS {0} / {1}` becomes `PLAYERS 3 / 6`).
4. Put the file in this folder, `BepInEx\HalfGripMods\Multiplayer\lang\`, and start the game. The log says `Translations: N row(s) from lang\french.csv`.

The file is a normal CSV: a comma or a semicolon between the columns, quotes around a text that has a comma in it. The first row is a header and is ignored.

Files in this folder do not change the mod's checksum, so players with different translations can still be in the same room.

Not translatable yet: sentences that have a name inside, such as "NAME joined." and "Invited NAME.", the room code and the checksum line, and what other players type in the chat.
