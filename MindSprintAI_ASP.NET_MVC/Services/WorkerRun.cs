namespace MindSprintAI_ASP.NET_MVC.Services;

// (NK1) Không kèm bản ghi đang xử lý: các worker có thể giữ địa chỉ email/bí mật trong bộ nhớ.
public sealed class WorkerRun : IDisposable
{
    private readonly ILogger logger;
    private readonly string worker;
    public int Count { get; set; }
    public bool Failed { get; set; }
    private bool finished;
    public WorkerRun(ILogger logger, string worker)
    {
        this.logger = logger; this.worker = worker;
        logger.LogInformation("Worker {Worker} run started", worker);
    }
    public void Dispose()
    {
        if (finished) return;
        finished = true;
        logger.LogInformation("Worker {Worker} run finished: {Count} items, failed={Failed}", worker, Count, Failed);
    }
}
