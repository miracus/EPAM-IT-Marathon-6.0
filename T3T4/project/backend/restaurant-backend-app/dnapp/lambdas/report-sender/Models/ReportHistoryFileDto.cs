namespace ReportSender.Models.Reports;

public class ReportHistoryFileDto
{
    public byte[] FileContent { get; set; } = [];

    public string ContentType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
}