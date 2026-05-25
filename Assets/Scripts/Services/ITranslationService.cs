// ITranslationService.cs
using System.Threading.Tasks;

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(string sourceText);
}


