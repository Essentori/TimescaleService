# Timescale CSV Processing Web API

Web API на **.NET 10** для загрузки CSV-файлов с подсчётом статистики и сохранение в **PostgreSQL**.

`/upload`: Принимает `.csv` (расширяемо, например, под `.json`) файл, проверяет данные, рассчитывает метрики (медиана, среднее, дельта) и сохраняет в базу данных </br>
`/results`: Возвращает список статистических результатов по выбранной фильтрации </br>
`/values/{filename}/last10`: Возвращает последние 10 показателей значений файла по имени, отсортированных по дате запуска

В файле `appsettings.json` в папке **TimescaleService** укажите вашу строку подключения к PostgreSQL:
```json
  "ConnectionStrings": {
    "Default": "Host=localhost;Port= ;Database=TimescaleServiceDb;Username= ;Password= "
  }
