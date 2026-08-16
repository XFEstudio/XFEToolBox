namespace XFEToolBox.Client.Utilities;

public static class TaskManager
{
    public static Dictionary<int, NamedTask> TaskDictionary { get; set; } = [];

    public static async Task Run(Action action, string taskName = "未命名任务")
    {
        var task = Task.Run(action);
        TaskDictionary.Add(task.Id, new(taskName, task));
        await task;
        TaskDictionary.Remove(task.Id);
    }

    public static async Task<TResult> Run<TResult>(Func<TResult> function, string taskName = "未命名任务")
    {
        var task = Task.Run(function);
        TaskDictionary.Add(task.Id, new(taskName, task));
        var result = await task;
        TaskDictionary.Remove(task.Id);
        return result;
    }
}