using System.Reflection;

namespace XFEToolBox.Utilities;

public static class ProfileHelper
{
    /// <summary>
    /// 设置配置文件值
    /// </summary>
    /// <param name="profilePath">配置文件属性路径</param>
    /// <param name="value">属性值</param>
    /// <exception cref="ArgumentException"></exception>
    public static void SetProfileValue(string profilePath, object value)
    {
        var splitPath = profilePath.Split('.');
        if (splitPath.Length < 2)
            throw new ArgumentException("Invalid property path. It should be in the format 'NameSpace.ClassName.PropertyName'.");
        string[] className = splitPath[..^1];
        var propertyName = splitPath[^1];
        var type = Type.GetType(string.Join(".", className)) ?? throw new ArgumentException($"Type '{className}' not found.");
        var propertyInfo = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public) ?? throw new ArgumentException($"Property '{propertyName}' not found on type '{className}'.");
        if (value is not null && !propertyInfo.PropertyType.IsAssignableFrom(value.GetType()))
            throw new ArgumentException($"Value is not assignable to property '{propertyName}' of type '{propertyInfo.PropertyType}'.");
        propertyInfo.SetValue(null, value);
    }
    /// <summary>
    /// 获取配置文件值
    /// </summary>
    /// <typeparam name="T">目标属性类型</typeparam>
    /// <param name="profilePath">配置文件属性路径</param>
    /// <returns></returns>
    public static T? GetProfileValue<T>(string profilePath)
    {
        var splitPath = profilePath.Split('.');
        if (splitPath.Length < 2)
            return default;
        string[] className = splitPath[..^1];
        var propertyName = splitPath[^1];
        var type = Type.GetType(string.Join(".", className));
        if (type is null)
            return default;
        var propertyInfo = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public);
        if (propertyInfo is null || !propertyInfo.PropertyType.IsAssignableFrom(typeof(T)))
            return default;
        var result = propertyInfo.GetValue(null);
        if (result is T resultT)
            return resultT;
        return default;
    }
}
