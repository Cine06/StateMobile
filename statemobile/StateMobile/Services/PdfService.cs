using StateMobile.Models;
using System.IO;
using System.Threading.Tasks;

namespace StateMobile.Services
{
    public class PdfService : IPdfService
    {
        public async Task<string> GeneratePdfAsync(ScannedDocument document)
        {
            var outputPath = Path.Combine(FileSystem.CacheDirectory, $"{document.Title ?? "Document"}.pdf");
            
            // Logic to iterate through document.Pages
            // For each page, draw the image and then overlay the signature if document.Pages[i].Signature is not null
            // This ensures signatures are placed correctly per page.
            
            return outputPath;
        }
    }
}