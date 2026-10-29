// Copied from F:\Git\halfGripTools\HalfGripTools\MiniJson.cs (the reader the game loader uses for mod.json), so a mod.json is judged here
// exactly as it will be judged in the game. Only the namespace changed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HalfGripModsInstaller
{
	/// <summary>
	/// A small, strict JSON reader for <c>mod.json</c> and <c>items/*.json</c>. Objects become <c>Dictionary&lt;string, object&gt;</c>,
	/// arrays <c>List&lt;object&gt;</c>, numbers <c>double</c>, plus <c>string</c>, <c>bool</c> and <c>null</c>.
	/// Errors say which line and column are wrong.
	/// </summary>
	internal sealed class MiniJson
	{
		private readonly string text;

		private int pos;

		private MiniJson(string text)
		{
			this.text = text;
		}

		/// <summary>Parses a whole document. Throws <see cref="FormatException"/> with a line and column if it is not valid JSON.</summary>
		internal static object Parse(string text)
		{
			MiniJson reader = new MiniJson(text);
			reader.SkipSpace();
			object value = reader.ReadValue();
			reader.SkipSpace();
			if (reader.pos < text.Length)
			{
				throw reader.Error("unexpected text after the end of the document");
			}
			return value;
		}

		/// <summary>Writes the same shapes <see cref="Parse"/> reads (dictionaries, lists, strings, numbers, bools, null) as indented JSON.</summary>
		internal static string Write(object value)
		{
			StringBuilder builder = new StringBuilder();
			WriteValue(builder, value, 0);
			return builder.Append('\n').ToString();
		}

		private static void WriteValue(StringBuilder builder, object value, int depth)
		{
			string indent = new string(' ', (depth + 1) * 2);
			switch (value)
			{
				case null:
					builder.Append("null");
					break;
				case string s:
					WriteString(builder, s);
					break;
				case bool b:
					builder.Append(b ? "true" : "false");
					break;
				case int i:
					builder.Append(i.ToString(CultureInfo.InvariantCulture));
					break;
				case double d:
					builder.Append(d.ToString("R", CultureInfo.InvariantCulture));
					break;
				case Dictionary<string, object> obj:
					if (obj.Count == 0)
					{
						builder.Append("{}");
						break;
					}
					builder.Append("{\n");
					int n = 0;
					foreach (KeyValuePair<string, object> pair in obj)
					{
						builder.Append(indent);
						WriteString(builder, pair.Key);
						builder.Append(": ");
						WriteValue(builder, pair.Value, depth + 1);
						builder.Append(++n < obj.Count ? ",\n" : "\n");
					}
					builder.Append(new string(' ', depth * 2)).Append('}');
					break;
				case List<object> list:
					if (list.Count == 0)
					{
						builder.Append("[]");
						break;
					}
					builder.Append("[\n");
					for (int k = 0; k < list.Count; k++)
					{
						builder.Append(indent);
						WriteValue(builder, list[k], depth + 1);
						builder.Append(k + 1 < list.Count ? ",\n" : "\n");
					}
					builder.Append(new string(' ', depth * 2)).Append(']');
					break;
				default:
					throw new ArgumentException("cannot write a " + value.GetType().Name + " as JSON");
			}
		}

		private static void WriteString(StringBuilder builder, string text)
		{
			builder.Append('"');
			foreach (char c in text)
			{
				switch (c)
				{
					case '"': builder.Append("\\\""); break;
					case '\\': builder.Append("\\\\"); break;
					case '\n': builder.Append("\\n"); break;
					case '\r': builder.Append("\\r"); break;
					case '\t': builder.Append("\\t"); break;
					default:
						if (c < ' ')
						{
							builder.Append("\\u").Append(((int)c).ToString("x4"));
						}
						else
						{
							builder.Append(c);
						}
						break;
				}
			}
			builder.Append('"');
		}

		internal static string String(Dictionary<string, object> obj, string key, string fallback = "")
		{
			return obj.TryGetValue(key, out object value) && value is string s ? s : fallback;
		}

		internal static float Number(Dictionary<string, object> obj, string key, float fallback = 0f)
		{
			return obj.TryGetValue(key, out object value) && value is double d ? (float)d : fallback;
		}

		internal static bool Bool(Dictionary<string, object> obj, string key, bool fallback = false)
		{
			return obj.TryGetValue(key, out object value) && value is bool b ? b : fallback;
		}

		internal static List<object> List(Dictionary<string, object> obj, string key)
		{
			return obj.TryGetValue(key, out object value) && value is List<object> list ? list : new List<object>();
		}

		private FormatException Error(string message)
		{
			int line = 1;
			int column = 1;
			for (int i = 0; i < pos && i < text.Length; i++)
			{
				if (text[i] == '\n')
				{
					line++;
					column = 1;
				}
				else
				{
					column++;
				}
			}
			return new FormatException($"{message} (line {line}, column {column})");
		}

		private void SkipSpace()
		{
			while (pos < text.Length && char.IsWhiteSpace(text[pos]))
			{
				pos++;
			}
		}

		private object ReadValue()
		{
			if (pos >= text.Length)
			{
				throw Error("the document ends too soon");
			}
			char c = text[pos];
			switch (c)
			{
				case '{':
					return ReadObject();
				case '[':
					return ReadArray();
				case '"':
					return ReadString();
				case 't':
					return Literal("true", true);
				case 'f':
					return Literal("false", false);
				case 'n':
					return Literal("null", null);
				default:
					if (c == '-' || (c >= '0' && c <= '9'))
					{
						return ReadNumber();
					}
					throw Error("unexpected '" + c + "'");
			}
		}

		private object Literal(string word, object value)
		{
			if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0)
			{
				throw Error("expected " + word);
			}
			pos += word.Length;
			return value;
		}

		private Dictionary<string, object> ReadObject()
		{
			Dictionary<string, object> result = new Dictionary<string, object>();
			pos++;
			SkipSpace();
			if (pos < text.Length && text[pos] == '}')
			{
				pos++;
				return result;
			}
			while (true)
			{
				SkipSpace();
				if (pos >= text.Length || text[pos] != '"')
				{
					throw Error("expected a quoted name");
				}
				string key = ReadString();
				SkipSpace();
				if (pos >= text.Length || text[pos] != ':')
				{
					throw Error("expected ':' after \"" + key + "\"");
				}
				pos++;
				SkipSpace();
				result[key] = ReadValue();
				SkipSpace();
				if (pos < text.Length && text[pos] == ',')
				{
					pos++;
					continue;
				}
				if (pos < text.Length && text[pos] == '}')
				{
					pos++;
					return result;
				}
				throw Error("expected ',' or '}'");
			}
		}

		private List<object> ReadArray()
		{
			List<object> result = new List<object>();
			pos++;
			SkipSpace();
			if (pos < text.Length && text[pos] == ']')
			{
				pos++;
				return result;
			}
			while (true)
			{
				SkipSpace();
				result.Add(ReadValue());
				SkipSpace();
				if (pos < text.Length && text[pos] == ',')
				{
					pos++;
					continue;
				}
				if (pos < text.Length && text[pos] == ']')
				{
					pos++;
					return result;
				}
				throw Error("expected ',' or ']'");
			}
		}

		private string ReadString()
		{
			StringBuilder builder = new StringBuilder();
			pos++;
			while (pos < text.Length)
			{
				char c = text[pos++];
				if (c == '"')
				{
					return builder.ToString();
				}
				if (c != '\\')
				{
					builder.Append(c);
					continue;
				}
				if (pos >= text.Length)
				{
					break;
				}
				char escape = text[pos++];
				switch (escape)
				{
					case '"': builder.Append('"'); break;
					case '\\': builder.Append('\\'); break;
					case '/': builder.Append('/'); break;
					case 'b': builder.Append('\b'); break;
					case 'f': builder.Append('\f'); break;
					case 'n': builder.Append('\n'); break;
					case 'r': builder.Append('\r'); break;
					case 't': builder.Append('\t'); break;
					case 'u':
						if (pos + 4 > text.Length || !ushort.TryParse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
						{
							throw Error("bad \\u escape");
						}
						builder.Append((char)code);
						pos += 4;
						break;
					default:
						pos--;
						throw Error("unknown escape \\" + escape);
				}
			}
			throw Error("a string is never closed");
		}

		private object ReadNumber()
		{
			int start = pos;
			while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '-' || text[pos] == '+' || text[pos] == '.' || text[pos] == 'e' || text[pos] == 'E'))
			{
				pos++;
			}
			if (!double.TryParse(text.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
			{
				pos = start;
				throw Error("not a number");
			}
			return number;
		}
	}
}
