using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Scenarios.Services;

namespace Scenarios.Controllers
{
    public class AsyncFactoryController : Controller
    {
        /// <summary>
        /// Это действие вызывает проблемы, поскольку оно пытается разрешить удаленное подключение из DI контейнера для чего требуется асинхронная операция
        /// Смотри Startup.cs для регистрации удаленного подключения.
         /// </summary>
        [HttpGet("/async-di-1")]
        public async Task<IActionResult> PublishAsync([FromServices]RemoteConnection remoteConnection)
        {
            await remoteConnection.PublishAsync("group", "hello");

            return Accepted();
        }

        [HttpGet("/async-di-2")]
        public async Task<IActionResult> PublishAsync([FromServices]RemoteConnectionFactory remoteConnectionFactory)
        {
            // Это не связано с проблемой взаимоблокировки, но каждый раз создает новое соединение
            var connection = await remoteConnectionFactory.ConnectAsync();

            await connection.PublishAsync("group", "hello");

            // Избавьтесь от созданного нами соединения
            await connection.DisposeAsync();

            return Accepted();
        }

        [HttpGet("/async-di-3")]
        public async Task<IActionResult> PublishAsync([FromServices]LoggingRemoteConnection remoteConnection)
        {
            // Это не связано с проблемой взаимоблокировки, но каждый раз создает новое соединение
            await remoteConnection.PublishAsync("group", "hello");

            return Accepted();
        }

        /// <summary>
        /// Это самый простой шаблон для работы с асинхронными конструкциями. 
        /// Реализация подключения немного сложнее, но использование выглядит как приоритетный метод, 
        /// который использует удаленное подключение, и он на самом деле безопасен.
        /// </summary>
        [HttpGet("/async-di-4")]
        public async Task<IActionResult> PublishAsync([FromServices]LazyRemoteConnection remoteConnection)
        {
            await remoteConnection.PublishAsync("group", "hello");

            return Accepted();
        }
    }
}
