using System.Threading.Tasks;
using ARLectureTranslator.Models;

namespace ARLectureTranslator.Services
{
    public interface IPipelineClient
    {
        Task<PipelineResponse> SendFrameAsync(
            string frameId,
            string imageBase64,
            int imageWidth,
            int imageHeight,
            string targetLanguage,
            bool mock
        );
    }
}
