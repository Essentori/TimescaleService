using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimescaleService.DataContext;
using TimescaleService.Services;

namespace TimescaleService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TimescaleController : ControllerBase
    {
        private IProcessingService csvService;
        private AppDatabaseContext context;
        public TimescaleController(IProcessingService csvService, AppDatabaseContext context)
        {
            this.csvService = csvService;
            this.context = context;
        }

        /// <summary>
        /// 1. Принимает на вход csv, после чего обрабатывает и сохраняет данных файла в БД
        /// </summary>
        [HttpPost("/upload")]
        public async Task<IActionResult> UploadCsv(IFormFile file)
        {
            if (file == null) 
                return BadRequest(new { error = "Файл не был загружен." });

            if (file.Length == 0)
                return BadRequest(new { error = "Загруженный файл пуст." });

            if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "Допускаются только файлы с расширением '.csv'." });

            using var stream = file.OpenReadStream();
            var (isSucceed, message) = await csvService.ProcessAsync(stream, file.FileName);

            if (!isSucceed)
                return BadRequest(new { error = message });
            else 
                return Ok(new { message = message });
        }

        /// <summary>
        /// 2. Получение списка записей из таблицы Results, подходящих под фильтры. 
        /// </summary>
        [HttpGet("/results")]
        public async Task<IActionResult> GetResults(
            // По имени файла
            [FromQuery] string? fileName,
            // По времени запуска первой операции (диапазон)
            [FromQuery] DateTime? minTimeFrom,
            [FromQuery] DateTime? minTimeTo,
            // По среднему показателю (диапазон)
            [FromQuery] double? avgValueFrom,
            [FromQuery] double? avgValueTo,
            // По среднему времени выполнению (диапазон)
            [FromQuery] double? avgExecutionTimeFrom,
            [FromQuery] double? avgExecutionTimeTo)
        {
            var resultsQuery = context.Results.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(fileName))
                resultsQuery = resultsQuery.Where(r => r.FileName.Contains(fileName));

            if (minTimeFrom.HasValue)
                resultsQuery = resultsQuery.Where(r => r.MinTime >= minTimeFrom.Value);
            if (minTimeTo.HasValue)
                resultsQuery = resultsQuery.Where(r => r.MinTime <= minTimeTo.Value);

            if (avgValueFrom.HasValue)
                resultsQuery = resultsQuery.Where(r => r.AvgValue >= avgValueFrom.Value);
            if (avgValueTo.HasValue)
                resultsQuery = resultsQuery.Where(r => r.AvgValue <= avgValueTo.Value);

            if (avgExecutionTimeFrom.HasValue)
                resultsQuery = resultsQuery.Where(r => r.AvgExecutionTime >= avgExecutionTimeFrom.Value);
            if (avgExecutionTimeTo.HasValue)
                resultsQuery = resultsQuery.Where(r => r.AvgExecutionTime <= avgExecutionTimeTo.Value);

            var result = await resultsQuery.ToListAsync();

            if (result.Count == 0)
                return BadRequest(new { error = "Не удалось найти данные по указанным фильтрам." });
            else return Ok(result);
        }

        /// <summary>
        /// 3. Получение списка последних 10 значений, отсортированных по начальному
        /// времени запуска Date по имени заданного файла.
        /// </summary>
        [HttpGet("/values/{filename}/last10")]
        public async Task<IActionResult> GetLast10Values(string filename)
        {
            //if (!filename.Contains('.'))
            //{
            //    return BadRequest(new { error = "Необходимо ввести расширешние файла." });
            //}
            var values = await context.Values
                                .AsNoTracking()
                                .Where(v => v.FileName == filename)
                                .OrderByDescending(v => v.Date)
                                .Take(10)
                                .ToListAsync();

            if (values.Count == 0)
                return NotFound(new { message = $"Записей для файла '{filename}' не найдено." });

            var formattedResponse = values.Select(v => new
            {
                Date = v.Date.ToString("yyyy-MM-dd HH:mm:ss.ffff"),
                v.Value
            });

            return Ok(formattedResponse);
        }
    }
}
