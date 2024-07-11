using System;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Koto.Configuration;
using Miningcore.Configuration;
using Miningcore.Mining;
using Miningcore.Notifications;
using Miningcore.Persistence;
using Miningcore.Stratum;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Miningcore.Blockchain.Koto
{
    [CoinFamily(CoinFamily.Koto)]
    public class KotoPool : PoolBase
    {
        private KotoJobManager manager;
        private KotoExtraNonceProvider extraNonceProvider;

        public KotoPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IMasterClock clock,
            NotificationService notificationService,
            IStatsRepository statsRepo) :
            base(ctx, serializerSettings, cf, clock, notificationService, statsRepo)
        {
        }

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<KotoJobManager>();
            extraNonceProvider = ctx.Resolve<KotoExtraNonceProvider>();
            manager.Configure(poolConfig, clusterConfig);
            await manager.StartAsync(ct);
        }

        protected override async Task OnRequestAsync(StratumConnection client, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var context = client.ContextAs<BitcoinWorkerContext>();
            var requestId = request.Value.Id;
            var method = request.Value.Method;

            switch (method)
            {
                case BitcoinStratumMethods.Subscribe:
                    await OnSubscribeAsync(ct, client, requestId, request.Value.Params);
                    break;

                case BitcoinStratumMethods.Authorize:
                    await OnAuthorizeAsync(ct, client, requestId, request.Value.Params);
                    break;

                case BitcoinStratumMethods.SubmitShare:
                    await OnSubmitAsync(ct, client, requestId, request.Value.Params);
                    break;

                case BitcoinStratumMethods.GetTransactions:
                    await OnGetTransactionsAsync(ct, client, requestId, request.Value.Params);
                    break;

                case BitcoinStratumMethods.GetJob:
                    await OnGetJobAsync(ct, client, requestId, request.Value.Params);
                    break;

                case BitcoinStratumMethods.MiningSubscribe:
                    await OnMiningSubscribeAsync(ct, client, requestId, request.Value.Params);
                    break;

                default:
                    logger.Warn(() => $"[{LogCategory}] Unsupported RPC request: {method}");
                    break;
            }
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var multiplier = BitcoinConstants.Pow2x32;
            var result = shares * multiplier / interval / 1000000 * 2;
        
            result /= hashrateDivisor;
            return result;
        }

        private async Task OnSubscribeAsync(CancellationToken ct, StratumConnection client, object requestId, JToken[] parameters)
        {
            var context = client.ContextAs<BitcoinWorkerContext>();

            var extraNonce1 = extraNonceProvider.Next();
            context.ExtraNonce1 = extraNonce1;
            context.ExtraNonce2Size = extraNonceProvider.Size;

            var response = new object[]
            {
                new object[]
                {
                    new object[] { BitcoinStratumMethods.MiningNotify, extraNonce1, context.ExtraNonce2Size }
                },
                context.SubscriberId
            };

            await client.RespondAsync(response, requestId, false);
        }

        private async Task OnAuthorizeAsync(CancellationToken ct, StratumConnection client, object requestId, JToken[] parameters)
        {
            var workerName = parameters[0].ToString();
            var password = parameters[1]?.ToString();

            var context = client.ContextAs<BitcoinWorkerContext>();
            context.IsAuthorized = true;
            context.MinerName = workerName;
            context.MinerPassword = password;

            await client.RespondAsync(true, requestId);
        }

        private async Task OnSubmitAsync(CancellationToken ct, StratumConnection client, object requestId, JToken[] parameters)
        {
            var context = client.ContextAs<BitcoinWorkerContext>();
            var workerName = context.MinerName;
            var extraNonce2 = parameters[1].ToString();
            var nTime = parameters[2].ToString();
            var nonce = parameters[3].ToString();

            // Submit share logic
            var share = await manager.SubmitShareAsync(client, workerName, extraNonce2, nTime, nonce, ct);

            if (share)
                await client.RespondAsync(true, requestId);
            else
                await client.RespondAsync(false, requestId);
        }

        private async Task OnGetTransactionsAsync(CancellationToken ct, StratumConnection client, object requestId, JToken[] parameters)
        {
            var context = client.ContextAs<BitcoinWorkerContext>();

            var transactions = manager.GetTransactionsForStratum();
            await client.RespondAsync(transactions, requestId);
        }

        private async Task OnGetJobAsync(CancellationToken ct, StratumConnection client, object requestId, JToken[] parameters)
        {
            var context = client.ContextAs<BitcoinWorkerContext>();
            var jobParams = manager.GetJobParamsForStratum(context.IsAuthorized);

            await client.RespondAsync(jobParams, requestId);
        }

        private async Task OnMiningSubscribeAsync(CancellationToken ct, StratumConnection client, object requestId, JToken[] parameters)
        {
            var context = client.ContextAs<BitcoinWorkerContext>();

            var extraNonce1 = extraNonceProvider.Next();
            context.ExtraNonce1 = extraNonce1;
            context.ExtraNonce2Size = extraNonceProvider.Size;

            var response = new object[]
            {
                new object[] { BitcoinStratumMethods.MiningNotify, extraNonce1, context.ExtraNonce2Size }
            };

            await client.RespondAsync(response, requestId, false);
        }
    }
}
