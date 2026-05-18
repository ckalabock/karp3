using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using HttpMonitorApp.Helpers;
using HttpMonitorApp.Models;
using HttpMonitorApp.Services;

namespace HttpMonitorApp.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _dashboardTimer;
    private readonly RequestMonitorService _monitorService;
    private readonly HttpMonitorServer _server;
    private readonly ClientRequestService _clientService;

    private string _serverPort = "8080";
    private string _clientUrl = "https://jsonplaceholder.typicode.com/posts/1";
    private string _selectedHttpMethod = "GET";
    private string _requestBody = "{\n  \"message\": \"Привет от WPF-клиента\"\n}";
    private string _responseBody = "Ответ появится здесь после отправки запроса.";
    private string _logsText = "Логи пока пусты.";
    private string _selectedLogMethodFilter = "Все";
    private string _selectedStatusFilter = "Все";
    private string _selectedGranularity = "Минуты";
    private string _statusMessage = "Сервер не запущен.";
    private string _serverStateText = "Остановлен";
    private bool _isServerRunning;

    public MainViewModel()
    {
        var logFilePath = Path.Combine(AppContext.BaseDirectory, "logs.txt");
        _monitorService = new RequestMonitorService(logFilePath);
        _server = new HttpMonitorServer(_monitorService);
        _clientService = new ClientRequestService(_monitorService);

        HttpMethods = new ObservableCollection<string>(["GET", "POST"]);
        LogMethodFilters = new ObservableCollection<string>(["Все", "GET", "POST"]);
        StatusFilters = new ObservableCollection<string>(["Все", "2xx", "4xx", "5xx", "Ошибка"]);
        GranularityOptions = new ObservableCollection<string>(["Минуты", "Часы"]);
        Statistics = new ObservableCollection<MetricRow>();
        LoadChart = new ObservableCollection<LoadChartBar>();

        StartServerCommand = new AsyncRelayCommand(StartServerAsync, () => !IsServerRunning);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync, () => IsServerRunning);
        SendRequestCommand = new AsyncRelayCommand(SendRequestAsync);

        _monitorService.LogRecorded += OnLogRecorded;

        _dashboardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _dashboardTimer.Tick += (_, _) => RefreshDashboard();
        _dashboardTimer.Start();

        RefreshDashboard();
        RefreshLogsText();
    }

    public ObservableCollection<string> HttpMethods { get; }

    public ObservableCollection<string> LogMethodFilters { get; }

    public ObservableCollection<string> StatusFilters { get; }

    public ObservableCollection<string> GranularityOptions { get; }

    public ObservableCollection<MetricRow> Statistics { get; }

    public ObservableCollection<LoadChartBar> LoadChart { get; }

    public AsyncRelayCommand StartServerCommand { get; }

    public AsyncRelayCommand StopServerCommand { get; }

    public AsyncRelayCommand SendRequestCommand { get; }

    public string ServerPort
    {
        get => _serverPort;
        set => SetProperty(ref _serverPort, value);
    }

    public string ClientUrl
    {
        get => _clientUrl;
        set => SetProperty(ref _clientUrl, value);
    }

    public string SelectedHttpMethod
    {
        get => _selectedHttpMethod;
        set => SetProperty(ref _selectedHttpMethod, value);
    }

    public string RequestBody
    {
        get => _requestBody;
        set => SetProperty(ref _requestBody, value);
    }

    public string ResponseBody
    {
        get => _responseBody;
        set => SetProperty(ref _responseBody, value);
    }

    public string LogsText
    {
        get => _logsText;
        private set => SetProperty(ref _logsText, value);
    }

    public string SelectedLogMethodFilter
    {
        get => _selectedLogMethodFilter;
        set
        {
            if (SetProperty(ref _selectedLogMethodFilter, value))
            {
                RefreshLogsText();
            }
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                RefreshLogsText();
            }
        }
    }

    public string SelectedGranularity
    {
        get => _selectedGranularity;
        set
        {
            if (SetProperty(ref _selectedGranularity, value))
            {
                RefreshChart();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ServerStateText
    {
        get => _serverStateText;
        private set => SetProperty(ref _serverStateText, value);
    }

    public string LogFilePath => _monitorService.LogFilePath;

    public bool IsServerRunning
    {
        get => _isServerRunning;
        private set
        {
            if (SetProperty(ref _isServerRunning, value))
            {
                ServerStateText = value ? $"Работает на порту {_server.CurrentPort}" : "Остановлен";
                StartServerCommand.RaiseCanExecuteChanged();
                StopServerCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task DisposeAsync()
    {
        _dashboardTimer.Stop();
        _monitorService.LogRecorded -= OnLogRecorded;
        await _server.StopAsync();
    }

    private async Task StartServerAsync()
    {
        if (!int.TryParse(ServerPort, out var port) || port is < 1 or > 65535)
        {
            StatusMessage = "Укажите корректный порт в диапазоне 1-65535.";
            return;
        }

        try
        {
            await _server.StartAsync(port);
            IsServerRunning = true;
            StatusMessage = $"Сервер запущен: http://localhost:{port}/";
            RefreshDashboard();
        }
        catch (HttpListenerException exception)
        {
            StatusMessage = $"Не удалось запустить сервер: {exception.Message}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ошибка запуска сервера: {exception.Message}";
        }
    }

    private async Task StopServerAsync()
    {
        await _server.StopAsync();
        IsServerRunning = false;
        StatusMessage = "Сервер остановлен.";
        RefreshDashboard();
    }

    private async Task SendRequestAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientUrl))
        {
            ResponseBody = "Укажите URL для отправки запроса.";
            return;
        }

        var requestBody = SelectedHttpMethod == "POST" ? RequestBody : string.Empty;
        var result = await _clientService.SendAsync(ClientUrl.Trim(), SelectedHttpMethod, requestBody);
        var builder = new StringBuilder();
        builder.AppendLine($"Статус: {(result.StatusCode == 0 ? "Ошибка" : result.StatusCode)}");

        if (!string.IsNullOrWhiteSpace(result.ResponseHeaders))
        {
            builder.AppendLine("Заголовки ответа:");
            builder.AppendLine(result.ResponseHeaders);
            builder.AppendLine();
        }

        builder.AppendLine("Тело ответа:");
        builder.AppendLine(result.ResponseBody);

        ResponseBody = builder.ToString().Trim();
        StatusMessage = result.IsError
            ? "Клиентский запрос завершился с ошибкой."
            : "Клиентский запрос успешно выполнен.";
    }

    private void OnLogRecorded(LogEntry entry)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            RefreshLogsText();
            RefreshDashboard();
        });
    }

    private void RefreshDashboard()
    {
        var snapshot = _monitorService.CreateSnapshot(_server.Uptime, _server.StoredMessagesCount);

        Statistics.Clear();
        Statistics.Add(new MetricRow { Name = "Время работы", Value = snapshot.Uptime.ToString(@"dd\.hh\:mm\:ss") });
        Statistics.Add(new MetricRow { Name = "Всего входящих запросов", Value = snapshot.TotalIncomingRequests.ToString() });
        Statistics.Add(new MetricRow { Name = "GET-запросы", Value = snapshot.GetRequests.ToString() });
        Statistics.Add(new MetricRow { Name = "POST-запросы", Value = snapshot.PostRequests.ToString() });
        Statistics.Add(new MetricRow { Name = "Среднее время обработки", Value = $"{snapshot.AverageProcessingTimeMs:F2} мс" });
        Statistics.Add(new MetricRow { Name = "Сохранено сообщений", Value = snapshot.StoredMessages.ToString() });

        if (IsServerRunning)
        {
            ServerStateText = $"Работает на порту {_server.CurrentPort}";
        }

        RefreshChart();
    }

    private void RefreshChart()
    {
        var granularity = SelectedGranularity == "Часы" ? LoadGranularity.Hour : LoadGranularity.Minute;
        var points = _monitorService.BuildLoadPoints(granularity);
        var maxValue = Math.Max(1, points.Max(point => point.Count));

        LoadChart.Clear();

        foreach (var point in points)
        {
            var height = point.Count == 0 ? 10 : 30 + (120.0 * point.Count / maxValue);
            LoadChart.Add(new LoadChartBar
            {
                Label = point.Label,
                Count = point.Count,
                Height = height
            });
        }
    }

    private void RefreshLogsText()
    {
        var filteredLogs = _monitorService
            .GetLogs()
            .Where(log => SelectedLogMethodFilter == "Все" || log.Method == SelectedLogMethodFilter)
            .Where(log => SelectedStatusFilter == "Все" || log.StatusCategory == SelectedStatusFilter)
            .ToList();

        if (filteredLogs.Count == 0)
        {
            LogsText = "Нет логов для выбранных фильтров.";
            return;
        }

        var builder = new StringBuilder();

        foreach (var log in filteredLogs)
        {
            builder.AppendLine($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] {(log.Direction == LogDirection.Incoming ? "IN" : "OUT")} {log.Method} {log.Url}");
            builder.AppendLine($"Статус: {log.StatusDisplay} | Время: {log.DurationMs} мс");
            builder.AppendLine("Заголовки:");
            builder.AppendLine(string.IsNullOrWhiteSpace(log.Headers) ? "<empty>" : log.Headers);
            builder.AppendLine("Тело запроса:");
            builder.AppendLine(string.IsNullOrWhiteSpace(log.RequestBody) ? "<empty>" : log.RequestBody);
            builder.AppendLine("Заголовки ответа:");
            builder.AppendLine(string.IsNullOrWhiteSpace(log.ResponseHeaders) ? "<empty>" : log.ResponseHeaders);
            builder.AppendLine("Тело ответа:");
            builder.AppendLine(string.IsNullOrWhiteSpace(log.ResponseBody) ? "<empty>" : log.ResponseBody);
            builder.AppendLine(new string('=', 96));
        }

        LogsText = builder.ToString().TrimEnd();
    }
}
