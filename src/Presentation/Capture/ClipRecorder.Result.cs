namespace UNNAMED.Presentation.Capture;

public sealed partial class ClipRecorder
{
    public sealed record Result(string Path, int VideoFrames, long AudioFrames, bool Finalised, string? Problem)
    {
        /// <summary>Every terminal writer state releases the recording UI, including an unexpected cleanup fault.</summary>
        public static Result? Poll(Task<Result> writing, string path) => !writing.IsCompleted ? null
            : writing.IsCompletedSuccessfully ? writing.Result
            : new Result(path, 0, 0, false, writing.Exception?.GetBaseException().Message ?? "Clip recording was canceled.");
    }
}
