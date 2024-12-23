namespace System.Threading.Tasks
{
    public static class TaskExtensions
    {
        /// <summary>
        /// Таймер не будет удален до тех пор, пока не сработает этот токен. 
        /// Если это токен с длительным сроком службы, это может привести к утечке памяти и исчерпанию очереди таймера!
        /// </summary>
        public static async Task<T> WithCancellationBad<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var delayTask = Task.Delay(-1, cancellationToken);

            var resultTask = await Task.WhenAny(task, delayTask);
            if (resultTask == delayTask)
            {
                // Операция отменена
                throw new OperationCanceledException();
            }

            return await task;
        }

        /// <summary>
        /// Это правильно регистрирует и отменяет регистрацию токена при завершении одной из операци
        /// </summary>
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Это отменяет регистрацию, как только запускается одна из задач
            using (cancellationToken.Register(state =>
            {
                ((TaskCompletionSource<object>)state).TrySetResult(null);
            },
            tcs))
            {
                var resultTask = await Task.WhenAny(task, tcs.Task);
                if (resultTask == tcs.Task)
                {
                    // Операция отменена
                    throw new OperationCanceledException(cancellationToken);
                }

                return await task;
            }
        }

        /// <summary>
        /// Этот метод не отменяет таймер, даже если операция успешно завершена.
        /// Это означает, что вы можете столкнуться с переполнением очереди по таймеру!
        /// </summary>
        public static async Task<T> TimeoutAfterBad<T>(this Task<T> task, TimeSpan timeout)
        {
            var delayTask = Task.Delay(timeout);

            var resultTask = await Task.WhenAny(task, delayTask);
            if (resultTask == delayTask)
            {
                // Операция отменена
                throw new OperationCanceledException();
            }

            return await task;
        }

        /// <summary>
        /// Этот метод отменяет действие таймера, если операция успешно завершена.
        /// </summary>
        public static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout)
        {
            using (var cts = new CancellationTokenSource())
            {
                var delayTask = Task.Delay(timeout, cts.Token);

                var resultTask = await Task.WhenAny(task, delayTask);
                if (resultTask == delayTask)
                {
                    // Операция отменена
                    throw new OperationCanceledException();
                }
                else
                {
                    // Отмените задание таймера, чтобы оно не сработало
                    cts.Cancel();
                }

                return await task;
            }
        }
    }
}
