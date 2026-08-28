namespace XFEToolBox.Client.Utilities;

public static class TaskManager
{
    public static async Task Run(Action action, string taskName = "未命名任务")
    {
        using var activity = ActivityCenterService.Start(taskName);
        try
        {
            await Task.Run(action);
            activity.Succeed();
        }
        catch (Exception exception)
        {
            activity.Fail(exception.Message);
            throw;
        }
    }

    public static async Task<TResult> Run<TResult>(Func<TResult> function, string taskName = "未命名任务")
    {
        using var activity = ActivityCenterService.Start(taskName);
        try
        {
            var result = await Task.Run(function);
            activity.Succeed();
            return result;
        }
        catch (Exception exception)
        {
            activity.Fail(exception.Message);
            throw;
        }
    }
}
