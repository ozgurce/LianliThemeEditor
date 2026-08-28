namespace ThemeEditorCSharp.Services;

public interface IGallerySubmissionService
{
    Task<string> SubmitAsync(GallerySubmission submission, CancellationToken cancellationToken = default);
}
