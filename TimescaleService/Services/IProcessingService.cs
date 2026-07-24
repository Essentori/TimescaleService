namespace TimescaleService.Services
{
    public interface IProcessingService
    {
        Task<(bool IsSucceed, string Message)> 
            ProcessAsync(Stream fileStream, string fileName);
    }
}
