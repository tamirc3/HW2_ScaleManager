namespace ScaleManager;

public class WorkerRequestMessage
{
    public HashRequest HashRequest { get; set; }
    public string Id { get; set; }
    public DateTime StartTime { get; set; }
    public WorkerRequestMessage(HashRequest hashRequest)
    {
        HashRequest = hashRequest;
        Id = Guid.NewGuid().ToString();
        StartTime = DateTime.UtcNow;
    }

}