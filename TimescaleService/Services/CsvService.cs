using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TimescaleService.DataContext;
using TimescaleService.Models;

namespace TimescaleService.Services
{
    public class CsvService : IProcessingService
    {
        private AppDatabaseContext context;
        // В случае, если нужно будет добавить новое свойство (колонку) в класс (таблицу)
        // private int expectedColoumnAmount = typeof(ValuesItem).GetProperties().Length - 2;
        private int expectedColoumnAmount = 3;
        private const string dateFormat = "yyyy-MM-dd'T'HH-mm-ss.ffff'Z'";

        public CsvService(AppDatabaseContext databaseContext) 
            => context = databaseContext;

        public async Task<(bool IsSucceed, string Message)> ProcessAsync(Stream fileStream, string fileName)
        {
            //await context.Database.OpenConnectionAsync();
            //IDbContextTransaction _transaction;
            //try
            //{
            //    _transaction = await context.Database.BeginTransactionAsync();
            //}
            //catch
            //{
            //    return (false, "Ошибка: " +
            //    "Не удалось подключиться к базе данных.");
            //}
            //using var transaction = _transaction;
            try
            {
                using var reader = new StreamReader(fileStream);

                var valuesList = new List<ValuesItem>();
                var valuesForMedian = new List<double>();

                double executionTimeSum = 0;
                double valueSum = 0;
                double maxValue = double.MinValue;
                double minValue = double.MaxValue;
                DateTime minTime = DateTime.MaxValue;
                DateTime maxTime = DateTime.MinValue;

                // Дата не может быть позже текущей и раньше 01.01.2000
                DateTime minDate_validation = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime maxDate_validation = DateTime.UtcNow;

                int lineCount = 0;
                string? line;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    lineCount++;

                    // Количество строк не может быть меньше 1 и больше 10 000
                    if (lineCount > 10000)
                    {
                        return ThrowError("Ошибка: " +
                            "Количество строк в файле превышает 10000.");
                    }

                    var lineValues = line.Split(';');

                    //Значения должны соответствовать своим типам, отсутствие одного из значений в записи недопустимо
                    if (lineValues.Length != expectedColoumnAmount)
                    {
                        return ThrowError($"Ошибка в строке {lineCount}: " +
                            $"Неверный формат строки. " +
                            $"Ожиидалось полей: {expectedColoumnAmount}, получено: {lineValues.Length}");
                    }

                    var date = lineValues[0];
                    var executionTime = lineValues[1];
                    var value = lineValues[2];

                    // Валидация Date
                    bool dateIsValid = DateTime.TryParseExact(
                                                date,
                                                dateFormat,
                                                CultureInfo.InvariantCulture,
                                                DateTimeStyles.AdjustToUniversal,
                                                out DateTime dateValue);

                    if (!dateIsValid)
                    {
                        return ThrowError($"Ошибка в строке {lineCount}: " +
                            $"Некорректный формат даты '{date}' (требуемый формат: ГГГГ-ММ-ДДTчч-мм-сс.ммммZ).");
                    }
                    if (dateValue < minDate_validation || dateValue > maxDate_validation)
                    {
                        return ThrowError($"Ошибка в строке {lineCount}: " +
                            $"Дата '{date}' выходит за диапазон между 01.01.2000 и текущем временем.");
                    }

                    // Валидация ExecutionTime
                    double executionTimeValue = double.NaN;
                    bool executionTimeIsValid = double.TryParse(
                                                executionTime, 
                                                CultureInfo.InvariantCulture, 
                                                out executionTimeValue);
                    if(executionTimeIsValid)
                        executionTimeIsValid = !(double.IsNaN(executionTimeValue) || double.IsInfinity(executionTimeValue));

                    if (!executionTimeIsValid)
                    {
                        return ThrowError($"Ошибка в строке {lineCount}: " +
                            $"Некорректное значение ExecutionTime '{executionTime}'.");
                    }

                        // Время выполнения не может быть меньше 0
                    if (executionTimeValue < 0)
                    {
                        return ThrowError($"Ошибка в строке {lineCount}: " +
                            $"Значение ExecutionTime не может быть отрицательным ({executionTimeValue} < 0).");
                    }

                    // Валидация Value
                    double valueNumber;
                    bool valueIsValid = double.TryParse(
                                        value, 
                                        CultureInfo.InvariantCulture, 
                                        out valueNumber);
                    if(valueIsValid)
                        valueIsValid = !(double.IsNaN(valueNumber) || double.IsInfinity(valueNumber));

                    if (!valueIsValid)
                    {
                        return ThrowError($"Ошибка в строке {lineCount}: " +
                            $"Некорректное значение Value '{value}'.");
                    }
                        // Значение показателя не может быть меньше 0
                    if (valueNumber < 0)
                    {
                        return ThrowError($"Ошибка в строке {lineCount}: " +
                            $"Значение Value не может быть отрицательным ({valueNumber} < 0).");
                    }

                    valuesList.Add(new ValuesItem
                    {
                        FileName = fileName,
                        Date = dateValue,
                        ExecutionTime = executionTimeValue,
                        Value = valueNumber
                    });

                    if (dateValue < minTime) minTime = dateValue;
                    if (dateValue > maxTime) maxTime = dateValue;
                    if (valueNumber < minValue) minValue = valueNumber;
                    if (valueNumber > maxValue) maxValue = valueNumber;

                    executionTimeSum += executionTimeValue;
                    valueSum += valueNumber;

                    valuesForMedian.Add(valueNumber);
                }

                if (lineCount == 0)
                {
                    return ThrowError("Ошибка: " +
                        "Файл пуст (0 строк).");
                }

                // Если файл с таким именем уже существует, необходимо перезаписывать значения в базе.
                var currentValues = await context.Values.Where(v => v.FileName == fileName).ToListAsync();
                var currentResults = await context.Results.Where(r => r.FileName == fileName).ToListAsync();

                bool isOverwritten = currentValues.Count > 0 || currentResults.Count > 0;

                if(isOverwritten)
                {
                    context.Values.RemoveRange(currentValues);
                    context.Results.RemoveRange(currentResults);
                }

                // Также из значений файла подсчитываются интегральные результаты
                double timeDelta = (maxTime - minTime).TotalSeconds;
                double avgExecutionTime = executionTimeSum / lineCount;
                double avgValue = valueSum / lineCount;
                double median = CalculateMedian(valuesForMedian);

                var resultItem = new ResultsItem
                {
                    FileName = fileName,
                    DeltaTime = timeDelta,
                    MinTime = minTime,
                    AvgExecutionTime = avgExecutionTime,
                    AvgValue = avgValue,
                    MedianValue = median,
                    MaxValue = maxValue,
                    MinValue = minValue
                };

                await context.Values.AddRangeAsync(valuesList);
                await context.Results.AddAsync(resultItem);

                await context.SaveChangesAsync();

                return HandleSucceedProccessing(fileName, isOverwritten, valuesList.Count);
            }
            //Если какое-либо условие не выполнено, нужно считать файл невалидным, откатить изменения и вернуть
            //пользователю соовтетствующую ошибку.
            catch (Exception ex)
            {
                return ThrowError($"Ошибка обработки файла: {ex.Message}");
            }
        }
        private (bool, string) ThrowError(string errorMessage)
            => (false, errorMessage);
        private (bool, string) HandleSucceedProccessing(string fileName, bool isOverwritten, int valuesAmount)
        {
            string message = isOverwritten ?
                $"Данные '{fileName}' успешно перезаписаны и сохранены." :
                $"Файл '{fileName}' успешно обработан и сохранён. Добавлено данных в БД: {valuesAmount}";
            return (true, message);
        }
        private double CalculateMedian(List<double> values)
        {
            values.Sort();
            int count = values.Count;

            if (count % 2 == 0)
            {
                return (values[count / 2 - 1] + values[count / 2]) / 2d;
            }
            else return values[count / 2];
        }
    }
}
