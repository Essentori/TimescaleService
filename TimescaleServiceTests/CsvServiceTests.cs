using Microsoft.EntityFrameworkCore;
using System.Text;
using TimescaleService.DataContext;
using TimescaleService.Services;

namespace TimescaleServiceTests
{
    public class CsvServiceTests
    {
        private AppDatabaseContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new AppDatabaseContext(options);
        }

        #region Валидный .csv файл
        [Fact]
        public async Task Test_SavesDataAndCalculatesCorrectResults()
        {
            using var context = GetInMemoryDbContext();
            var service = new CsvService(context);

            string fileName = "test-Correct.csv";
            string csvText =
                "2026-01-01T10-00-00.0000Z;2;10\n" +
                "2026-01-01T10-00-10.0000Z;4.5;20.5\n" +
                "2026-01-01T10-00-20.0000Z;6.4;30.4";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));

            var result = await service.ProcessAsync(stream, fileName);

            Assert.True(result.IsSucceed);
            Assert.NotEmpty(result.Message);

            var savedValues = await context.Values.Where(v => v.FileName == fileName).ToListAsync();
            Assert.Equal(3, savedValues.Count);

            var savedResult = await context.Results.FirstOrDefaultAsync(r => r.FileName == fileName);
            Assert.NotNull(savedResult);
            Assert.Equal(20d, savedResult.DeltaTime); // 20 секунд между первой и последней датой
            Assert.Equal(4.3d, savedResult.AvgExecutionTime); // (2 + 4.5 + 6.4) / 3 = 4.3
            Assert.Equal(20.3d, savedResult.AvgValue); // (10 + 20.5 + 30.4) / 3 = 20,3
            Assert.Equal(20.5d, savedResult.MedianValue); // Медиана 10, 20.5, 30.4 = 20.5
        }
        #endregion

        #region Дата не может быть позже текущей и раньше 01.01.2000
        [Fact]
        public async Task Test_InvalidDateReturnsErrorAndDoesNotSaveData()
        {
            using var context = GetInMemoryDbContext();
            var service = new CsvService(context);

            string fileName = "test-InvalidDate.csv";
            string csvText = "2027-01-01T10-00-00.0000Z;1;10";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));

            var result = await service.ProcessAsync(stream, fileName);

            Assert.False(result.IsSucceed);
            Assert.Contains("выходит за диапазон", result.Message);

            Assert.Empty(context.Values);
            Assert.Empty(context.Results);
        }
        #endregion

        #region Время выполнения не может быть меньше 0
        [Fact]
        public async Task Test_NegativeExecutionTimReturnsErrorAndDoesNotSaveData()
        {
            using var context = GetInMemoryDbContext();
            var service = new CsvService(context);

            string fileName = "test-NegativeExecutionTime.csv";
            string csvText = "2026-01-01T10-00-00.0000Z;-2;10";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));

            var result = await service.ProcessAsync(stream, fileName);

            Assert.False(result.IsSucceed);
            Assert.Contains("не может быть отрицательным", result.Message);

            Assert.Empty(context.Values);
            Assert.Empty(context.Results);
        }
        #endregion

        #region Значение показателя не может быть меньше 0
        [Fact]
        public async Task Test_NegativeValueReturnsErrorAndDoesNotSaveData()
        {
            using var context = GetInMemoryDbContext();
            var service = new CsvService(context);

            string fileName = "test-NegativeValue.csv";
            string csvText = "2026-01-01T10-00-00.0000Z;2;-10";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));

            var result = await service.ProcessAsync(stream, fileName);

            Assert.False(result.IsSucceed);
            Assert.Contains("не может быть отрицательным", result.Message);

            Assert.Empty(context.Values);
            Assert.Empty(context.Results);
        }
        #endregion

        #region Количество строк не может быть меньше 1 (и больше 10 000)
        [Fact]
        public async Task Test_InvalidLinesAmountReturnsErrorAndDoesNotSaveData()
        {
            using var context = GetInMemoryDbContext();
            var service = new CsvService(context);

            string fileName = "test-InvalidLinesAmount.csv";
            string csvText = string.Empty;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));

            var result = await service.ProcessAsync(stream, fileName);

            Assert.False(result.IsSucceed);
            Assert.Contains("Файл пуст", result.Message);

            Assert.Empty(context.Values);
            Assert.Empty(context.Results);
        }
        #endregion

        #region Значения должны соответствовать своим типам, отсутствие одного из значений в записи недопустимо
        [Fact]
        public async Task Test_InvalidLinesFormatReturnsErrorAndDoesNotSaveData()
        {
            using var context = GetInMemoryDbContext();
            var service = new CsvService(context);

            string fileName = "test-InvalidLinesFormat.csv";
            string csvText = 
                "2026-01-01T10-00-00.0000Z;1\n" +
                "2026-01-01T10-00-00.0000Z;3;30;30";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));

            var result = await service.ProcessAsync(stream, fileName);

            Assert.False(result.IsSucceed);
            Assert.Contains("формат", result.Message);

            Assert.Empty(context.Values);
            Assert.Empty(context.Results);
        }
        #endregion

        #region Если файл с таким именем уже существует, необходимо перезаписывать значения в базе
        [Fact]
        public async Task Test_ReuploadingSameFileOverwritesOldData()
        {
            using var context = GetInMemoryDbContext();
            var service = new CsvService(context);

            string fileName = "test-Overwrite.csv";
            string initialCsv = "2026-01-01T10-00-00.0000Z;2;10";

            using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(initialCsv));
            await service.ProcessAsync(stream1, fileName);

            string newCsv = "2026-01-01T10-10-00.0000Z;2;20";

            using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(newCsv));
            var result = await service.ProcessAsync(stream2, fileName);

            Assert.True(result.IsSucceed);

            var savedValues = await context.Values.Where(v => v.FileName == fileName).ToListAsync();
            // Первое значение показателя должно перезаписаться с 10 на 20
            Assert.Equal(20d, savedValues[0].Value);

            var savedResult = await context.Results.FirstOrDefaultAsync(r => r.FileName == fileName);
            Assert.NotNull(savedResult);
            Assert.Equal(20d, savedResult.AvgValue);
        }
        #endregion
    }
}
