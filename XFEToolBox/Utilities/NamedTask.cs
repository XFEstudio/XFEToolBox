namespace XFEToolBox.Utilities;

public class NamedTask(string name, Task task)
{
    public string Name { get; set; } = name;
    public Task Task { get; set; } = task;
}
