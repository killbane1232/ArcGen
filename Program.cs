using Arcam.Data;
using Arcam.Data.DataBase;
using Arcam.Data.DataBase.DBTypes;
using Arcam.Data.DataTypes;
using Arcam.Indicators;
using ArcCore.PPO;
using NLog;
using System.Text;
using Arcam.Market;
using Arcam.Data.ClickHouse.DBTypes;
using Arcam.Data.ClickHouse;
using Arcam.Main;
using System.Text.Json;

namespace ArcamFullPick
{
    public class ArcamFullPick
    {
        static Dictionary<string, double> hashs = [];
        static Logger logger;
        public static void Main(string[] args)
        {
            Arcam.Main.Loggers.LoggerConfigurator.Configure(Arcam.Main.Loggers.LoggerConfigurator.LogType.test);
            logger = LogManager.GetCurrentClassLogger();
            PPOModel();
        }

        public static void ExportCandlesToJson()
        {
            Strategy strat;
            using (var dba = new ProdContext())
            {
                strat = dba.Strategy.Where(x => x.Id == 21).ToList()[0];
                dba.Entry(strat).Reference(x => x.Timing).Load();
                dba.Entry(strat).Reference(x => x.Pair).Load();
                dba.Entry(strat.Pair).Reference(x => x.MainCurrency).Load();
                dba.Entry(strat.Pair).Reference(x => x.EncountingType).Load();
                dba.Entry(strat.Pair).Reference(x => x.Platform).Load();
                dba.Entry(strat).Collection(x => x.StrategyIndicators).Load();

                foreach (var indicator in strat.StrategyIndicators)
                {
                    indicator.Strategy = strat;
                    dba.Entry(indicator).Reference(x => x.Indicator).Load();
                    var fields = dba.InputField.Where(x => x.StrategyIndicatorId == indicator.Id).ToList();
                    foreach (var field in fields)
                    {
                        dba.Entry(field).Reference(x => x.IndicatorField).Load();
                        indicator.InputFields[field.IndicatorField.CodeName!] = field;
                    }
                }
            }

            using var db = new TestContext();
            strat = strat.CreateCopy(db);

            var loader = new TestDataLoader(strat);
            var candles = loader.GetData();
            candles.RemoveAt(0);
            for (var i = 0; i < candles.Count; i++)
            {
                candles[i].TimeStamp = candles[i].TimeStamp.AddMinutes(-1);
            }
            candles = candles.Where(x => x.TimeStamp.Year == 2025).ToList();

            var options = new JsonSerializerOptions();

            var jsonString = JsonSerializer.Serialize(candles, options);
            File.WriteAllText("candles2025.json", jsonString);
            logger.Debug("Свечи успешно экспортированы в candles.json");
        }

        public static void PPOModel()
        {
            Strategy strat;
            using (var dba = new ProdContext())
            {
                strat = dba.Strategy.Where(x => x.Id == 21).ToList()[0];
                dba.Entry(strat).Reference(x => x.Timing).Load();
                dba.Entry(strat).Reference(x => x.Pair).Load();
                dba.Entry(strat.Pair).Reference(x => x.MainCurrency).Load();
                dba.Entry(strat.Pair).Reference(x => x.EncountingType).Load();
                dba.Entry(strat.Pair).Reference(x => x.Platform).Load();
                dba.Entry(strat).Collection(x => x.StrategyIndicators).Load();

                foreach (var indicator in strat.StrategyIndicators)
                {
                    indicator.Strategy = strat;
                    dba.Entry(indicator).Reference(x => x.Indicator).Load();
                    var fields = dba.InputField.Where(x => x.StrategyIndicatorId == indicator.Id).ToList();
                    foreach (var field in fields)
                    {
                        dba.Entry(field).Reference(x => x.IndicatorField).Load();
                        indicator.InputFields[field.IndicatorField.CodeName!] = field;
                    }
                }
            }

            using var db = new TestContext();
            strat = strat.CreateCopy(db);

            var loader = new TestDataLoader(strat);
            TestStrategy? testStrategy = null;
            testStrategy = new TestStrategy
            {
                Strategy = strat,
                Date = DateTime.Now,
                StrategyId = strat.Id,
                StrategyHash = strat.GetHashCode(),
                BaseStrategyId = strat.Id
            };

            var candles = loader.GetData();
            candles.RemoveAt(0);
            for (var i = 0; i < candles.Count; i++)
            {
                candles[i].TimeStamp = candles[i].TimeStamp.AddMinutes(-1);
            }
            candles = candles.Where(x => x.TimeStamp.Year == 2025).ToList();



            var ppo = new PPO();
            var episodes = 1000;
            var bestReward = double.MinValue;
            var bestModelPath = "C:\\git\\Arcam\\PPO\\models\\2025-06-20__3__window200\\actor_model_2025-06-20__3__window200.onnx";

            logger.Debug($"Start learn");
            var totalReward = 0.0;
            // Проходим по всем свечам
            var platf = new FakePlatform(testStrategy, new List<Candle>(candles));
            var previous_state = platf._value;
            while (!platf.end)
            {
                var currentCandles = platf.TakeCandles(strat.Pair, 200, strat.Timing);
                var allPos = platf.EncountPositions();
                var reward = 0.0;
                PositionInfo currentPosition = new PositionInfo
                {
                    InPosition = false
                };
                if (allPos.ContainsKey(strat.Pair.Name))
                    currentPosition = allPos[strat.Pair.Name];

                // Получаем действие от модели
                var actions = ppo.GetAction(currentCandles, currentPosition.InPosition ? 1 : 0, currentPosition.IsLong ? 1 : 0, (float)currentPosition.Price);
                var action = Array.FindIndex(actions, x => x == actions.Max());

                // Вычисляем награду на основе изменения цены
                if (action == 0)
                {
                    if (currentPosition.InPosition)
                    {
                        if (!currentPosition.IsLong)
                             platf.ClosePosition(strat.Pair, 0, false);
                    }
                    else
                        platf.PostOrders(true, 0, strat.Pair); // Сигнал на покупку
                }
                else if (action == 1)
                {
                    if (currentPosition.InPosition)
                    {
                        if (currentPosition.IsLong)
                            platf.ClosePosition(strat.Pair, 0, false);
                    }
                    else
                        platf.PostOrders(false, 0, strat.Pair); // Сигнал на покупку
                } else if (action == 2)
                {
                    if (currentPosition.InPosition)
                    {
                        platf.ClosePosition(strat.Pair, 0, false);
                    }
                }
                reward = platf._value - previous_state;

                // Обновляем список свечей
                platf.NextCheckDate();

                totalReward += reward;
            }

            // Логируем результаты
            logger.Debug($"Total Reward: {totalReward} final value: {platf._value}");
            IndicatorsPicker.WriteToExcel(testStrategy);

            // Сохраняем лучшую модель
            if (totalReward > bestReward)
            {
                bestReward = totalReward;
                // Здесь нужно добавить сохранение модели
                //ExportPPOModels.ExportModels(DateTime.Today.ToShortDateString() + "__" + episode);
                logger.Debug($"New best model saved with reward: {bestReward}");
            }
            
            logger.Debug($"Training completed. Best reward: {bestReward}");
        }

        public static void GAModel() 
        {
            var flag = false;
            Strategy strat;
            using (var dba = new ProdContext())
            {
                strat = dba.Strategy.Where(x => x.Id == 21).ToList()[0];
                dba.Entry(strat).Reference(x => x.Timing).Load();
                dba.Entry(strat).Reference(x => x.Pair).Load();
                dba.Entry(strat.Pair).Reference(x => x.MainCurrency).Load();
                dba.Entry(strat.Pair).Reference(x => x.EncountingType).Load();
                dba.Entry(strat.Pair).Reference(x => x.Platform).Load();
                dba.Entry(strat).Collection(x => x.StrategyIndicators).Load();

                foreach (var indicator in strat.StrategyIndicators)
                {
                    indicator.Strategy = strat;
                    dba.Entry(indicator).Reference(x => x.Indicator).Load();
                    var fields = dba.InputField.Where(x => x.StrategyIndicatorId == indicator.Id).ToList();
                    foreach (var field in fields)
                    {
                        dba.Entry(field).Reference(x => x.IndicatorField).Load();
                        indicator.InputFields[field.IndicatorField.CodeName!] = field;
                    }
                }
            }

            using var db = new TestContext();
            strat = strat.CreateCopy(db);


            var ga = new GA();
            var mask = new StrategyMask(strat);

            var firstParent = mask.GetParams(strat);

            var currentGeneration = ga.GenerateInitGeneration(firstParent);

            var loader = new TestDataLoader(strat);
            var candles = loader.GetData();
            var bestStrat = new List<int>();

            candles.RemoveAt(0);
            for (var i = 0; i < candles.Count; i++)
            {
                candles[i].TimeStamp = candles[i].TimeStamp.AddMinutes(-1);
            }

            var picker = new IndicatorsPicker();
            var generationCount = 0;
            double bestResult = 0;
            int countBestRepeat = 0;
            int parallelCNT = 10;
            while (generationCount < 1000)
            {
                logger.Debug("Generation: " + generationCount + " BestResult: " + bestResult);
                var i = 0;
                var threadList = new Task[parallelCNT];
                while (i < currentGeneration.Count)
                {
                    for (var j = 0; j < parallelCNT; j++)
                    {
                        threadList[j] = Task.CompletedTask;
                    }
                    for (var j = 0; j < parallelCNT && i < currentGeneration.Count; j++)
                    {
                        while (i < currentGeneration.Count && hashs.ContainsKey(GetHash(currentGeneration[i])))
                        {
                            i++;
                        }
                        if (i >= currentGeneration.Count)
                            break;
                        lock (hashs)
                        {
                            hashs[GetHash(currentGeneration[i])] = 0;
                        }
                        int r = i;
                        var task = new Task(() =>
                        {
                            var myHash = GetHash(currentGeneration[r]);
                            var strat1 = mask.GetStrategy(currentGeneration[r]);
                            var res = picker.SilentPickIndicators(strat1, candles, 45);
                            lock (hashs)
                            {
                                hashs[myHash] = res;
                            }
                        });
                        task.Start();
                        Thread.Sleep(10);
                        threadList[j] = task;
                        i++;
                    }
                    bool taskflag = false;
                    while (!taskflag)
                    {
                        Task.WaitAny(threadList);
                        var completedCnt = 0;
                        for (var idxRun = 0; idxRun < threadList.Length; idxRun++)
                            if (threadList[idxRun].IsCompleted || threadList[idxRun].IsCompletedSuccessfully || threadList[idxRun].IsFaulted || threadList[idxRun].IsCanceled)
                                completedCnt++;
                        taskflag = completedCnt == threadList.Length;
                    }
                    foreach (var task in threadList)
                        task.Dispose();
                }

                var resList = new List<GA.GAPriority>();
                double newBest = 0;
                int newBestIdx = 0;
                for (var j = 0; j < currentGeneration.Count; j++)
                {
                    var hash = GetHash(currentGeneration[j]);
                    newBest = Math.Max(hashs[hash], newBest);
                    newBestIdx = j;
                    resList.Add(new GA.GAPriority()
                    {
                        param = currentGeneration[j],
                        priority = hashs[hash]
                    });
                }
                if (Math.Round(newBest) <= Math.Round(bestResult))
                    countBestRepeat++;
                else
                {
                    if (bestResult < newBest)
                    {
                        bestStrat = currentGeneration[newBestIdx];
                    }
                    bestResult = Math.Max(newBest, bestResult);
                    countBestRepeat = 0;
                }
                currentGeneration = ga.GetNextGeneration(resList, countBestRepeat > 10);
                if (countBestRepeat > 10)
                {
                    countBestRepeat = 0;
                }
                generationCount++;
            }

            strat = mask.GetStrategy(bestStrat);
            strat = strat.CreateCopy(new ProdContext());
            logger.Debug($"Generation: {generationCount} BestResult: {bestResult} Id: {strat.Id}");
        }

        public static string GetHash(List<int> differ)
        {
            var str = new StringBuilder();
            foreach (var diff in differ)
            {
                str.Append(diff);
            }
            return str.ToString();
        }
    }
}