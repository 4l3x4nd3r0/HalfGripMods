namespace Multiplayer
{
	/// <summary>Short names for <c>ctx.Text</c> (the loader merges <c>lang\*.csv</c> into the game's text table). The English text is the key.</summary>
	internal static class Tx
	{
		internal static string T(string english)
		{
			return Mod.Ctx.Text.T(english);
		}

		internal static string F(string english, params object[] args)
		{
			return Mod.Ctx.Text.F(english, args);
		}
	}
}
