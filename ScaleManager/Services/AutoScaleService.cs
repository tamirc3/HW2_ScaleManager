namespace ScaleManager.Services
{
    public class AutoScaleService
    {
        private readonly IAppServiceManager _appServiceManager;
        private readonly string _queueHost;

        public AutoScaleService(IAppServiceManager appServiceManager, string queueHost)
        {
            _appServiceManager = appServiceManager;
            _queueHost = queueHost;
        }
        public async void AutoScaleCheck(object? o)
        {
            while (true)
            {
                var queueStatics = await GetQueueStatics();

                if (queueStatics.TooManyLongWaitingMessages())
                {
                    await _appServiceManager.CreateAppServiceAsync();
                    await Task.Delay(60 * 1000); // let give the new app service some time to work
                }
                else if (queueStatics.MessagesAreProcessedFastEnough())
                {
                    await _appServiceManager.DeleteAppService();
                }
            }
        }

        private  async Task<QueueStatics> GetQueueStatics()
        {
            QueueStatics queueStatics = new QueueStatics();

            int seconds = 0;
            while (seconds <= 60 || !queueStatics.TooManyLongWaitingMessages())
            {
                seconds++;

                var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(_queueHost+"/Queue/workerQueue/oldestMessageWaitingTimeInSeconds");
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var oldestMessageTime = (int)Double.Parse(responseContent);

                    if (oldestMessageTime >= QueueStatics.HighWaitingTime)
                    {
                        queueStatics.HighThresholdCount++;
                    }
                    else
                    {
                        queueStatics.LowThresholdCount++;
                    }

                }
            }
            return queueStatics;
        }



        public class QueueStatics
        {
            public int HighThresholdCount { get; set; }
            public int LowThresholdCount { get; set; }

            private const int HighThreshold = 20;
            private const int LowThreshold = 45;

            public static double HighWaitingTime = 3.0;
            public static double LowWaitingTime = 1.0;

            public bool TooManyLongWaitingMessages()
            {
                return HighThresholdCount >= HighThreshold;
            }
            public bool MessagesAreProcessedFastEnough()
            {
                return LowThresholdCount >= LowThreshold;
            }
        }

        public void Start()
        {
            var cancellationTokenSource = new CancellationTokenSource();
            Task.Factory.StartNew(AutoScaleCheck
                , TaskCreationOptions.LongRunning
                , cancellationTokenSource.Token);
        }
    }
}
