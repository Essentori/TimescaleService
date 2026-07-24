namespace TimescaleService.Models
{
    public class ResultsItem
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        // Date
        public double DeltaTime { get; set; }
        public DateTime MinTime { get; set; }
        // ExecutionTime
        public double AvgExecutionTime { get; set; }
        // Value
        public double AvgValue { get; set; }
        public double MedianValue { get; set; }
        public double MaxValue { get; set; }
        public double MinValue { get; set; }
    }
}
