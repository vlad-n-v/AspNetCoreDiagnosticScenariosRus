using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scenarios.Model;

namespace Scenarios.Controllers
{
    public class FireAndForgetController : Controller
    {
        [HttpGet("/fire-and-forget-1")]
        public IActionResult FireAndForget1([FromServices]PokemonDbContext context)
        {
            // Это неявный асинхронный метод void. 
            // ThreadPool.QueueUserWorkItem выполняет действие, но компилятор позволяет использовать вместо него асинхронные делегаты void. 
            // Это опасно, поскольку необработанные исключения приведут к остановке всего серверного процесса.
            //
            // This is an implicit async void method. ThreadPool.QueueUserWorkItem takes an Action, but the compiler allows
            // async void delegates to be used in its place. This is dangerous because unhandled exceptions will bring down the entire server process.
            ThreadPool.QueueUserWorkItem(async state =>
            {
                await Task.Delay(1000);

                // Это замыкание захватывает контекст из параметра Controller action. 
                // Это плохо, потому что этот рабочий элемент может выполняться за пределами области запроса, а Pokemon DbContext ограничен областью запроса. 
                // В результате это приведет к сбою процесса с помощью исключения ObjectDisposedException
                //
                // This closure is capturing the context from the Controller action parameter. This is bad because this work item could run
                // outside of the request scope and the PokemonDbContext is scoped to the request. As a result, this will crash the process with
                // and ObjectDisposedException
                context.Pokemon.Add(new Pokemon());
                await context.SaveChangesAsync();
            });

            return Accepted();
        }


        [HttpGet("/fire-and-forget-2")]
        public IActionResult FireAndForget2([FromServices]PokemonDbContext context)
        {
            // Здесь используется Task.Run вместо ThreadPool.QueueUserWorkItem. 
            // В основном это эквивалентно Fire And Forget 1, но поскольку мы используем AsyncTask вместо async void, необработанные исключения не приведут к сбою процесса. 
            // Однако они запускают TaskScheduler.Событие unbservedtaskexception, когда исключения остаются необработанными.
            //
            // This uses Task.Run instead of ThreadPool.QueueUserWorkItem. It's mostly equivalent to the FireAndForget1 but since we're using 
            // async Task instead of async void, unhandled exceptions won't crash the process. They will however trigger the TaskScheduler.UnobservedTaskException
            // event when exceptions go unhandled.
            Task.Run(async () =>
            {
                await Task.Delay(1000);

                // Это замыкание захватывает контекст из параметра Controller action. 
                // Это плохо, потому что этот рабочий элемент может выполняться за пределами области запроса, а Pokemon DbContext ограничен областью запроса. 
                // В результате это вызовет необработанное исключение ObjectDisposedException.
                //
                // This closure is capturing the context from the Controller action parameter. This is bad because this work item could run
                // outside of the request scope and the PokemonDbContext is scoped to the request. As a result, this will throw an unhandled ObjectDisposedException.
                context.Pokemon.Add(new Pokemon());
                await context.SaveChangesAsync();
            });

            return Accepted();
        }

        [HttpGet("/fire-and-forget-3")]

        public IActionResult FireAndForget3([FromServices]IServiceScopeFactory serviceScopeFactory)
        {
            // В этой версии FireAndForget добавлена некоторая обработка исключений. 
            // Мы также больше не перехватываем Pokemon DbContext из входящего запроса.
            // Вместо этого мы вводим IServiceScopeFactory (который является синглтоном), чтобы создать область действия в фоновом рабочем элементе.
            //
            // This version of fire and forget adds some exception handling. We're also no longer capturing the PokemonDbContext from the incoming request.
            // Instead, we're injecting an IServiceScopeFactory (which is a singleton) in order to create a scope in the background work item.
            Task.Run(async () =>
            {
                await Task.Delay(1000);

                // Создайте область действия на время действия фоновой операции и разрешите использование служб на ее основе
                //
                // Create a scope for the lifetime of the background operation and resolve services from it
                using (var scope = serviceScopeFactory.CreateScope())
                {
                    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("Background Task");

                    // ЭТО ОПАСНО! Мы перехватываем HttpContext из входящего запроса при закрытии, которое запускает фоновый рабочий элемент. 
                    // Это не сработает, потому что входящий http-запрос завершится до того, как рабочий элемент будет выполнен.
                    //
                    // THIS IS DANGEROUS! We're capturing the HttpContext from the incoming request in the closure that
                    // runs the background work item. This will not work because the incoming http request will be over before
                    // the work item executes.
                    using (logger.BeginScope("Background operation kicked off from {RequestId}", HttpContext.TraceIdentifier))
                    {
                        try
                        {
                            // Это приведет к появлению PokemonDbContext из правильной области видимости, и операция завершится успешно
                            //
                            // This will a PokemonDbContext from the correct scope and the operation will succeed
                            var context = scope.ServiceProvider.GetRequiredService<PokemonDbContext>();

                            context.Pokemon.Add(new Pokemon());
                            await context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Background task failed.");
                        }
                    }
                }
            });

            return Accepted();
        }

        [HttpGet("/fire-and-forget-4")]
        public IActionResult FireAndForget4([FromServices]IServiceScopeFactory serviceScopeFactory)
        {
            Task.Run(async () =>
            {
                await Task.Delay(1000);

                using (var scope = serviceScopeFactory.CreateScope())
                {
                    // Вместо захвата HttpContext из свойства controller мы используем IHttpContextAccessor
                    //
                    // Instead of capturing the HttpContext from the controller property, we use the IHttpContextAccessor
                    var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
                    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

                    var logger = loggerFactory.CreateLogger("Background Task");

                    // ЭТО ОПАСНО! Мы пытаемся использовать HttpContext из входящего запроса в замыкании, которое запускает фоновый рабочий элемент. 
                    // Это не сработает, потому что входящий http-запрос завершится до того, как рабочий элемент будет выполнен.
                    //
                    // THIS IS DANGEROUS! We're trying to use the HttpContext from the incoming request in the closure that
                    // runs the background work item. This will not work because the incoming http request will be over before
                    // the work item executes.
                    using (logger.BeginScope("Background operation kicked off from {RequestId}", accessor.HttpContext.TraceIdentifier))
                    {
                        try
                        {

                            var context = scope.ServiceProvider.GetRequiredService<PokemonDbContext>();

                            context.Pokemon.Add(new Pokemon());
                            await context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Background task failed.");
                        }
                    }
                }
            });

            return Accepted();
        }

        [HttpGet("/fire-and-forget-5")]
        public IActionResult FireAndForget5([FromServices]IServiceScopeFactory serviceScopeFactory)
        {
            // Сначала запишите идентификатор трассировки
            //
            // Capture the trace identifier first
            string traceIdenifier = HttpContext.TraceIdentifier;

            Task.Run(async () =>
            {
                await Task.Delay(1000);

                using (var scope = serviceScopeFactory.CreateScope())
                {
                    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

                    var logger = loggerFactory.CreateLogger("Background Task");

                    // При этом используется идентификатор трассировки, полученный в момент запуска запроса.
                    // This uses the traceIdenifier captured at the time the request started.
                    using (logger.BeginScope("Background operation kicked off from {RequestId}", traceIdenifier))
                    {
                        try
                        {

                            var context = scope.ServiceProvider.GetRequiredService<PokemonDbContext>();

                            context.Add(new Pokemon());
                            await context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Background task failed.");
                        }
                    }
                }
            });

            return Accepted();
        }
    }
}
