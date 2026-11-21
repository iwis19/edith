namespace Edith.Look
{
    public enum LookPhase { Looking, Naming, Named, Done, Failed }

    /// What a Look's card knows. Events from the backend move it along: Looking until a name starts to arrive,
    /// Naming while the name is still being written, Named once it is final and the fact types out, then Done, or
    /// Failed with a line to show. The card shows the fact, or the name when the Look ends without one. The rarity
    /// can land at any time before the end, and so can a hunt tick. Nothing changes it after it ends.
    public sealed class LookCard
    {
        // A 150-character fact types out in about a second, which reads as being written.
        const float CharsPerSecond = 150f;

        public LookPhase Phase { get; private set; }
        /// The backend's id for this Look, empty until it accepts the photo.
        public string LookRef { get; private set; } = "";
        public string Name { get; private set; } = "";
        /// Null until the backend rates the Look.
        public Rarity? Tier { get; private set; }
        /// The line to show once Failed.
        public string Error { get; private set; } = "";
        /// The hunt target this Look ticked, or null when it ticked none.
        public string HuntText { get; private set; }
        public TypedText Fact { get; } = new TypedText(CharsPerSecond);

        public bool Finished => Phase == LookPhase.Done || Phase == LookPhase.Failed;
        public bool Typing => !Fact.Done;

        public void Apply(in LookEvent e)
        {
            if (Finished) return;
            switch (e.Kind)
            {
                case LookEventKind.Accepted:
                    LookRef = e.LookRef ?? "";
                    break;
                case LookEventKind.Name:
                    if (Phase == LookPhase.Named && e.Pending) return;
                    Name = e.Name;
                    Phase = e.Pending ? LookPhase.Naming : LookPhase.Named;
                    break;
                case LookEventKind.Fact:
                    Fact.Set(e.Text);
                    Phase = LookPhase.Named;
                    break;
                case LookEventKind.Rarity:
                    Tier = e.Tier;
                    break;
                case LookEventKind.Hunt:
                    HuntText = e.Text;
                    break;
                case LookEventKind.Done:
                    // A Look with no fact shows its name instead, so the card is never blank.
                    if (Fact.Empty) Fact.Set(Name);
                    Phase = LookPhase.Done;
                    break;
                case LookEventKind.Error:
                    Error = e.Message;
                    Phase = LookPhase.Failed;
                    break;
            }
        }

        public void Advance(float seconds) => Fact.Advance(seconds);
    }
}
