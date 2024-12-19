# Содержание
- [Содержание](#содержание)
  - [Использование HttpClient](#использование-httpclient)
  - [Различные реализации платформы](#различные-реализации-платформы)
  - [Примечание о веб-клиенте](#примечание-о-веб-клиенте)
   
## Использование HttpClient

[HttpClient](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpclient?view=net-5.0) является основным API для отправки исходящих HTTP-запросов в .NET.

## Различные реализации платформы

`HttpClient` - это API-оболочка для `HttpMessageHandler`. Внутренности `HttpMessageHandler` - это то, что отвечает за отправку HTTP-запроса. Существует несколько реализаций на разных платформах .NET platforms. Этот документ посвящен серверным приложениям и будет посвящен 2-3 основным реализациям:
- HttpClientHandler/WebRequestHandler в .NET Framework
- SocketHttpHandler в .NET Core/5
- WinHttpHandler в .NET Framework или .NET Core/5 (работает на обоих, но зависит от Windows)

## Примечание о веб-клиенте

На данный момент WebClient считается устаревшим .NET API и был полностью заменен HttpClient. Новый код должен быть написан с помощью HttpClient.

❌ **BAD** В этом примере используется устаревший веб-клиент для выполнения асинхронного HTTP-запроса.

```C#
public string DoSomethingAsync()
{
    var client = new WebClient();
    return client.DownloadString("http://www.google.com");
}
```

:white_check_mark: **GOOD** В этом примере используется HttpClient для асинхронного выполнения HTTP-запроса.

```C#
static readonly HttpClient client = new HttpClient();

public async Task<string> DoSomethingAsync()
{
    return await client.GetStringAsync("http://www.google.com");
}
```
