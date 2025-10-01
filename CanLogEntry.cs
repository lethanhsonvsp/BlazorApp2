namespace BlazorApp2
{
    public class CanLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Direction { get; set; } = ""; // "TX" hoặc "RX"
        public string Frame { get; set; } = "";
    }

}
