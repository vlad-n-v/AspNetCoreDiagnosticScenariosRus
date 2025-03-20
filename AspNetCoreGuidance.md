# Содержание
 - [Руководство по ASP.NET Core](#руководство-по-aspnet-core)
   - [Избегайте использования синхронных методов Read/Write для HttpRequest.Body и HttpResponse.Body](#избегайте-использования-синхронных-методов-readwrite-для-httprequestbody-и-httpresponsebody)
   - [Предпочитайте использовать HttpRequest.ReadFormAsync() вместо HttpRequest.Form](#предпочитайте-использовать-httprequestreadformasync-вместо-httprequestform)
   - [Избегайте чтения больших тел запросов или ответов в память](#избегайте-чтения-больших-тел-запросов-или-ответов-в-память)
   - [Используйте буферизованное и синхронное чтение и запись как альтернативу асинхронному чтению и записи](#используйте-буферизованное-и-синхронное-чтение-и-запись-как-альтернативу-асинхронному-чтению-и-записи)
   - [Не сохраняйте IHttpContextAccessor.HttpContext в поле](#не-сохраняйте-ihttpcontextaccessorhttpcontext-в-поле)
   - [Не обращайтесь к HttpContext из нескольких потоков параллельно. Он не является потокобезопасным.](#не-обращайтесь-к-httpcontext-из-нескольких-потоков-параллельно-он-не-является-потокобезопасным)
   - [Не используйте HttpContext после завершения запроса](#не-используйте-httpcontext-после-завершения-запроса)
   - [Не захватывайте HttpContext в фоновых потоках](#не-захватывайте-httpcontext-в-фоновых-потоках)
   - [Не захватывайте сервисы, внедренные в контроллеры, в фоновых потоках](#не-захватывайте-сервисы-внедренные-в-контроллеры-в-фоновых-потоках)
   - [Избегайте добавления заголовков после запуска HttpResponse](#избегайте-добавления-заголовков-после-запуска-httpresponse)

# Руководство по ASP.NET Core

ASP.NET Core — это кроссплатформенный, высокопроизводительный фреймворк с открытым исходным кодом для создания современных облачных интернет-приложений. Это руководство описывает некоторые распространенные ловушки и практики при написании масштабируемых серверных приложений.

## Избегайте использования синхронных методов Read/Write для HttpRequest.Body и HttpResponse.Body

Весь ввод-вывод в ASP.NET Core асинхронный. Серверы реализуют интерфейс `Stream`, который имеет как синхронные, так и асинхронные перегрузки. Предпочтительнее использовать асинхронные перегрузки, чтобы избежать блокировки потоков из пула потоков (это может привести к истощению пула потоков).

❌ **ПЛОХО** В этом примере используется `StreamReader.ReadToEnd`, и в результате блокируется текущий поток в ожидании результата. Это пример [sync over async](AsyncGuidance.md#avoid-using-taskresult-and-taskwait).

```C#
public class MyController : Controller
{
    [HttpGet("/pokemon")]
    public ActionResult<PokemonData> Get()
    {
        // Это синхронно считывает всё тело HTTP-запроса в память.
        // Если клиент медленно загружает данные, мы выполняем sync over async, потому что Kestrel *НЕ* поддерживает синхронное чтение.
        var json = new StreamReader(Request.Body).ReadToEnd();

        return JsonConvert.DeserializeObject<PokemonData>(json);
    }
}
```

:white_check_mark: **ХОРОШО** В этом примере используется `StreamReader.ReadToEndAsync`, и в результате поток не блокируется во время чтения.

```C#
public class MyController : Controller
{
    [HttpGet("/pokemon")]
    public async Task<ActionResult<PokemonData>> Get()
    {
        // Это асинхронно считывает всё тело HTTP-запроса в память.
        var json = await new StreamReader(Request.Body).ReadToEndAsync();

        return JsonConvert.DeserializeObject<PokemonData>(json);
    }
}
```

:bulb:**ПРИМЕЧАНИЕ: Если запрос большой, это может привести к проблемам с памятью, что может вызвать атаку типа "отказ в обслуживании". Подробнее см. [здесь](#избегайте-чтения-больших-тел-запросов-или-ответов-в-память).**

## Предпочитайте использовать HttpRequest.ReadFormAsync() вместо HttpRequest.Form

Всегда предпочитайте `HttpRequest.ReadFormAsync()` вместо `HttpRequest.Form`. Единственный случай, когда безопасно использовать `HttpRequest.Form` — это когда форма уже была прочитана вызовом `HttpRequest.ReadFormAsync()`, и кэшированное значение формы читается с помощью `HttpRequest.Form`.

❌ **ПЛОХО** В этом примере HttpRequest.Form использует [sync over async](AsyncGuidance.md#avoid-using-taskresult-and-taskwait) под капотом и может привести к истощению пула потоков (в некоторых случаях).

```C#
public class MyController : Controller
{
    [HttpPost("/form-body")]
    public IActionResult Post()
    {
        var form = HttpRequest.Form;
        
        Process(form["id"], form["name"]);

        return Accepted();
    }
}
```

:white_check_mark: **ХОРОШО** В этом примере используется `HttpRequest.ReadFormAsync()` для асинхронного чтения тела формы.

```C#
public class MyController : Controller
{
    [HttpPost("/form-body")]
    public async Task<IActionResult> Post()
    {
        var form = await HttpRequest.ReadFormAsync();
        
        Process(form["id"], form["name"]);

        return Accepted();
    }
}
```

## Избегайте чтения больших тел запросов или ответов в память

В .NET любое выделение одиночного объекта размером более 85 КБ попадает в кучу больших объектов ([LOH](https://blogs.msdn.microsoft.com/maoni/2006/04/19/large-object-heap/)). Большие объекты дороги в двух отношениях:

- Стоимость выделения высока, поскольку память для вновь выделенного большого объекта должна быть очищена (CLR гарантирует, что память для всех вновь выделенных объектов очищается)
- LOH собирается вместе с остальной частью кучи (это требует "полной сборки мусора" или сборки поколения Gen2)

Эта [статья в блоге](https://adamsitnik.com/Array-Pool/#the-problem) кратко описывает проблему:

> Когда выделяется большой объект, он помечается как объект поколения Gen 2. Не Gen 0, как для маленьких объектов. Последствия таковы, что если у вас заканчивается память в LOH, сборщик мусора очищает всю управляемую кучу, а не только LOH. Поэтому он очищает Gen 0, Gen 1 и Gen 2, включая LOH. Это называется полной сборкой мусора и является наиболее затратной по времени. Для многих приложений это может быть приемлемо. Но определенно не для высокопроизводительных веб-серверов, где для обработки среднего веб-запроса требуется несколько больших буферов памяти (чтение из сокета, распаковка, декодирование JSON и др.).

Наивное сохранение большого тела запроса или ответа в одиночный `byte[]` или `string` может привести к быстрому исчерпанию пространства в LOH и вызвать проблемы с производительностью для вашего приложения из-за полных сборок мусора.

## Используйте буферизованное и синхронное чтение и запись как альтернативу асинхронному чтению и записи

При использовании сериализатора/десериализатора, который поддерживает только синхронное чтение и запись (например, JSON.NET), предпочтительнее буферизовать данные в память перед передачей данных в сериализатор/десериализатор.

:bulb:**ПРИМЕЧАНИЕ: Если запрос большой, это может привести к проблемам с памятью, что может вызвать атаку типа "отказ в обслуживании". Подробнее см. [здесь](#избегайте-чтения-больших-тел-запросов-или-ответов-в-память).**

## Не сохраняйте IHttpContextAccessor.HttpContext в поле

`IHttpContextAccessor.HttpContext` вернет `HttpContext` активного запроса при доступе из потока запроса. Его не следует хранить в поле или переменной.

❌ **ПЛОХО** В этом примере `HttpContext` сохраняется в поле, а затем происходит попытка использовать его позже.

```C#
public class MyType
{
    private readonly HttpContext _context;
    public MyType(IHttpContextAccessor accessor)
    {
        _context = accessor.HttpContext;
    }
    
    public void CheckAdmin()
    {
        if (!_context.User.IsInRole("admin"))
        {
            throw new UnauthorizedAccessException("The current user isn't an admin");
        }
    }
}
```

Приведенная выше логика, вероятно, захватит null или неверный `HttpContext` в конструкторе для последующего использования.

:white_check_mark: **ХОРОШО** В этом примере сам `IHttpContextAccessor` сохраняется в поле и используется поле `HttpContext` в нужное время (с проверкой на null).

```C#
public class MyType
{
    private readonly IHttpContextAccessor _accessor;
    public MyType(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }
    
    public void CheckAdmin()
    {
        var context = _accessor.HttpContext;
        if (context != null && !context.User.IsInRole("admin"))
        {
            throw new UnauthorizedAccessException("The current user isn't an admin");
        }
    }
}
```

## Не обращайтесь к HttpContext из нескольких потоков параллельно. Он не является потокобезопасным.

`HttpContext` *НЕ* является потокобезопасным. Доступ к нему из нескольких потоков параллельно может привести к повреждению, вызывающему неопределенное поведение (зависания, сбои, повреждение данных).

❌ **ПЛОХО** В этом примере выполняются 3 параллельных запроса и записывается путь входящего запроса до и после исходящего HTTP-запроса. Это обращается к пути запроса из нескольких потоков, потенциально параллельно.

```C#
public class AsyncController : Controller
{
    [HttpGet("/search")]
    public async Task<SearchResults> Get(string query)
    {
        var query1 = SearchAsync(SearchEngine.Google, query);
        var query2 = SearchAsync(SearchEngine.Bing, query);
        var query3 = SearchAsync(SearchEngine.DuckDuckGo, query);

        await Task.WhenAll(query1, query2, query3);
        
        var results1 = await query1;
        var results2 = await query2;
        var results3 = await query3;

        return SearchResults.Combine(results1, results2, results3);
    }

    private async Task<SearchResults> SearchAsync(SearchEngine engine, string query)
    {
        var searchResults = SearchResults.Empty;
        try
        {
            _logger.LogInformation("Starting search query from {path}.", HttpContext.Request.Path);
            searchResults = await _searchService.SearchAsync(engine, query);
            _logger.LogInformation("Finishing search query from {path}.", HttpContext.Request.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed query from {path}", HttpContext.Request.Path);
        }

        return searchResults;
    }
}
```

:white_check_mark: **ХОРОШО** В этом примере все данные из входящего запроса копируются перед выполнением 3 параллельных запросов.

```C#
public class AsyncController : Controller
{
    [HttpGet("/search")]
    public async Task<SearchResults> Get(string query)
    {
        string path = HttpContext.Request.Path;
        var query1 = SearchAsync(SearchEngine.Google, query, path);
        var query2 = SearchAsync(SearchEngine.Bing, query, path);
        var query3 = SearchAsync(SearchEngine.DuckDuckGo, query, path);

        await Task.WhenAll(query1, query2, query3);
        
        var results1 = await query1;
        var results2 = await query2;
        var results3 = await query3;

        return SearchResults.Combine(results1, results2, results3);
    }

    private async Task<SearchResults> SearchAsync(SearchEngine engine, string query, string path)
    {
        var searchResults = SearchResults.Empty;
        try
        {
            _logger.LogInformation("Starting search query from {path}.", path);
            searchResults = await _searchService.SearchAsync(engine, query);
            _logger.LogInformation("Finishing search query from {path}.", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed query from {path}", path);
        }

        return searchResults;
    }
}
```

## Не используйте HttpContext после завершения запроса

`HttpContext` действителен только до тех пор, пока активен HTTP-запрос. Весь конвейер ASP.NET Core — это асинхронная цепочка делегатов, которая выполняет каждый запрос. Когда `Task`, возвращаемый из этой цепочки, завершается, `HttpContext` переиспользуется.

❌ **ПЛОХО** В этом примере используется async void (что **ВСЕГДА** плохо в приложениях ASP.NET Core), и в результате происходит доступ к `HttpResponse` после завершения HTTP-запроса. Это приведет к сбою процесса.

```C#
public class AsyncVoidController : Controller
{
    [HttpGet("/async")]
    public async void Get()
    {
        await Task.Delay(1000);

        // ЭТО приведет к сбою процесса, так как мы пишем после того, как ответ завершился в фоновом потоке
        await Response.WriteAsync("Hello World");
    }
}
```

:white_check_mark: **ХОРОШО** В этом примере возвращается `Task` фреймворку, поэтому HTTP-запрос не завершается, пока не завершится всё действие.

```C#
public class AsyncController : Controller
{
    [HttpGet("/async")]
    public async Task Get()
    {
        await Task.Delay(1000);
        
        await Response.WriteAsync("Hello World");
    }
}
```

## Не захватывайте HttpContext в фоновых потоках

❌ **ПЛОХО** В этом примере показано, что замыкание захватывает `HttpContext` из свойства Controller. Это плохо, потому что этот рабочий элемент может выполняться вне области запроса и, как следствие, может привести к чтению неверного `HttpContext`.

```C#
[HttpGet("/fire-and-forget-1")]
public IActionResult FireAndForget1()
{
    _ = Task.Run(() =>
    {
        await Task.Delay(1000);

        // Это замыкание захватывает контекст из свойства Controller. Это плохо, потому что этот рабочий элемент может выполняться
        // вне HTTP-запроса, что приведет к чтению неверных данных.
        var path = HttpContext.Request.Path;
        Log(path);
    });

    return Accepted();
}
```

:white_check_mark: **ХОРОШО** В этом примере данные, необходимые в фоновой задаче, явно копируются во время запроса и не ссылаются на сам контроллер.

```C#
[HttpGet("/fire-and-forget-3")]
public IActionResult FireAndForget3()
{
    string path = HttpContext.Request.Path;
    _ = Task.Run(async () =>
    {
        await Task.Delay(1000);

        // Это захватывает только путь
        Log(path);
    });

    return Accepted();
}
```

## Не захватывайте сервисы, внедренные в контроллеры, в фоновых потоках

❌ **ПЛОХО** В этом примере показано, что замыкание захватывает `DbContext` из параметра действия Controller. Это плохо, потому что этот рабочий элемент может выполняться вне области запроса, а `PokemonDbContext` привязан к запросу. В результате это приведет к `ObjectDisposedException`.

```C#
[HttpGet("/fire-and-forget-1")]
public IActionResult FireAndForget1([FromServices]PokemonDbContext context)
{
    _ = Task.Run(() =>
    {
        await Task.Delay(1000);

        // Это замыкание захватывает контекст из параметра действия Controller. Это плохо, потому что этот рабочий элемент может выполняться
        // вне области запроса, а PokemonDbContext привязан к запросу. В результате это вызывает ObjectDisposedException
        context.Pokemon.Add(new Pokemon());
        await context.SaveChangesAsync();
    });

    return Accepted();
}
```

:white_check_mark: **ХОРОШО** В этом примере внедряется `IServiceScopeFactory` и создается новая область внедрения зависимостей в фоновом потоке, и не происходит ссылка на сам контроллер.

```C#
[HttpGet("/fire-and-forget-3")]
public IActionResult FireAndForget3([FromServices]IServiceScopeFactory serviceScopeFactory)
{
    // Эта версия "запустил и забыл" добавляет обработку исключений. Мы больше не захватываем PokemonDbContext из входящего запроса.
    // Вместо этого мы внедряем IServiceScopeFactory (который является синглтоном) для создания области в фоновом рабочем элементе.
    _ = Task.Run(async () =>
    {
        await Task.Delay(1000);

        // Создаем область на время фоновой операции и получаем из нее сервисы
        using (var scope = serviceScopeFactory.CreateScope())
        {
            // Это разрешит PokemonDbContext из правильной области, и операция выполнится успешно
            var context = scope.ServiceProvider.GetRequiredService<PokemonDbContext>();

            context.Pokemon.Add(new Pokemon());
            await context.SaveChangesAsync();
        }
    });

    return Accepted();
}
```

## Избегайте добавления заголовков после запуска HttpResponse

ASP.NET Core не буферизует тело HTTP-ответа. Это означает, что при первой записи ответа заголовки отправляются вместе с этой частью тела клиенту. Когда это происходит, уже невозможно изменить заголовки ответа.

❌ **ПЛОХО** Эта логика пытается добавить заголовки ответа после того, как ответ уже начал отправляться.

```C#
app.Use(async (next, context) =>
{
    await context.Response.WriteAsync("Hello ");
    
    await next();
    
    // Это может не сработать, если next() уже записал в ответ
    context.Response.Headers["test"] = "value";    
});
```

:white_check_mark: **ХОРОШО** В этом примере проверяется, начался ли HTTP-ответ, прежде чем писать в тело.

```C#
app.Use(async (next, context) =>
{
    await context.Response.WriteAsync("Hello ");
    
    await next();
    
    // Проверить, не начался ли уже ответ, прежде чем добавлять заголовок и писать
    if (!context.Response.HasStarted)
    {
        context.Response.Headers["test"] = "value";
    }
});
```

:white_check_mark: **ХОРОШО** В этом примере используется `HttpResponse.OnStarting` для установки заголовков до того, как заголовки ответа будут отправлены клиенту.

Это позволяет вам зарегистрировать обратный вызов, который будет вызван непосредственно перед записью заголовков ответа клиенту. Это дает возможность добавлять или переопределять заголовки в последний момент, не требуя знания о следующем промежуточном ПО в конвейере.

```C#
app.Use(async (next, context) =>
{
    // Настройка обратного вызова, который будет выполнен непосредственно перед отправкой заголовков ответа клиенту.
    context.Response.OnStarting(() => 
    {       
        context.Response.Headers["someheader"] = "somevalue"; 
        return Task.CompletedTask;
    });
    
    await next();
});
```
