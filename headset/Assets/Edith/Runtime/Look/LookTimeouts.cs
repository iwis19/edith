namespace Edith.Look
{
    /// How long a /look request may wait, in seconds.
    public readonly struct LookTimeouts
    {
        /// For the upload and the response headers.
        public readonly float Upload;
        /// From the response headers to the first event. The backend answers the moment it has the photo.
        public readonly float FirstEvent;
        /// From sending to the last event.
        public readonly float Total;

        public LookTimeouts(float upload, float firstEvent, float total)
        {
            Upload = upload;
            FirstEvent = firstEvent;
            Total = total;
        }

        public static LookTimeouts Default => new LookTimeouts(5f, 4f, 20f);
    }
}
