namespace BlazorApp2
{
    public class CanLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Direction { get; set; } = ""; // "TX", "RX" hoặc "SYS"
        public string Frame { get; set; } = "";
        public string Msg { get; set; } = "";      
    }
}
