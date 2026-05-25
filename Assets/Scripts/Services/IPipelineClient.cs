using System.Threading.Tasks;

public interface IPipelineClient
{
    Task<PipelineResponse> SendFrameAsync(
        string frameId,
        string imageBase64,
        int imageWidth,
        int imageHeight,
        string targetLanguage,
        bool mock,
        string ocrProvider = "",
        string translationProvider = ""
    );
}
