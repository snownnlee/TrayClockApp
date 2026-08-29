using System.Collections.Concurrent;
using System.Windows;

namespace TrayClockApp.Core;

public static class EventBus
{
    private static readonly ConcurrentDictionary<EventType, List<Action<object?, object?>>> EventActions = new();

    public static void Register(EventType eventType, Action<object?, object?> handler)
    {
        EventActions.AddOrUpdate(eventType,
            _ => [handler],
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(handler);
                }

                return list;
            });
    }

    public static void Publish(EventType eventType, object? source = null, object? argument = null)
    {
        if (Application.Current is not null)
            Application.Current.Dispatcher.InvokeAsync(() => Dispatch(eventType, source, argument));
        else
            Task.Run(() => Dispatch(eventType, source, argument));
    }

    private static void Dispatch(EventType eventType, object? source, object? argument)
    {
        if (!EventActions.TryGetValue(eventType, out var handlers)) return;
        foreach (var handler in handlers)
            try
            {
                handler(source, argument);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EventBus] handler error: {ex}");
            }
    }

    public static void Clear()
    {
        EventActions.Clear();
    }
}