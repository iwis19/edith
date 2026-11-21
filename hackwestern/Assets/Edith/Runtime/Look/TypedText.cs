using System;

namespace Edith.Look
{
    /// Reveals text a few characters at a time, so a fact that arrives all at once still reads as being written. Text
    /// that extends what is already showing keeps typing from where it is; any other text starts over.
    public sealed class TypedText
    {
        // Unity UI rich text has no escape for "<", so a model's "<" is shown as a look-alike that cannot open a tag.
        const char SafeLessThan = '‹';
        const string HiddenOpen = "<color=#00000000>", HiddenClose = "</color>";

        readonly float charsPerSecond;
        string target = "";
        float shown;

        public TypedText(float charsPerSecond) => this.charsPerSecond = charsPerSecond;

        public string Visible => target.Substring(0, Math.Min((int)shown, target.Length));
        public bool Done => (int)shown >= target.Length;
        public bool Empty => target.Length == 0;

        /// The whole text as rich text, with the part not yet typed drawn fully transparent. The layout is the final
        /// one from the first character, so wrapped lines do not shift while typing.
        public string Render()
        {
            var visible = Visible;
            return visible.Length == target.Length ? visible : visible + HiddenOpen + target.Substring(visible.Length) + HiddenClose;
        }

        public void Set(string text)
        {
            var visible = Visible;
            target = (text ?? "").Replace('<', SafeLessThan);
            if (!target.StartsWith(visible, StringComparison.Ordinal)) shown = 0f;
        }

        public void Advance(float seconds) => shown = Math.Min(target.Length, shown + seconds * charsPerSecond);
    }
}
