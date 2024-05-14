using Arcam.Data;
using Arcam.Data.DataBase;
using Arcam.Indicators;
using NLog;
using System.Text;

namespace ArcamFullPick
{
    public class ArcamFullPick
    {
        static Dictionary<string, double> hashs = [];
        public static void Main(string[] args)
        {
            Arcam.Main.Loggers.LoggerConfigurator.Configure(Arcam.Main.Loggers.LoggerConfigurator.LogType.test);
            var logger = LogManager.GetCurrentClassLogger();
            using var db = new ApplicationContext();
            var flag = false;

            var strat = db.Strategy.Where(x => x.Id == 25605).ToList()[0];
            db.Entry(strat).Reference(x => x.Timing).Load();
            db.Entry(strat).Reference(x => x.Pair).Load();
            db.Entry(strat.Pair).Reference(x => x.MainCurrency).Load();
            db.Entry(strat.Pair).Reference(x => x.EncountingType).Load();
            db.Entry(strat).Collection(x => x.StrategyIndicators).Load();

            foreach (var indicator in strat.StrategyIndicators)
            {
                indicator.Strategy = strat;
                db.Entry(indicator).Reference(x => x.Indicator).Load();
                var fields = db.InputField.Where(x => x.StrategyIndicatorId == indicator.Id).ToList();
                foreach (var field in fields)
                {
                    db.Entry(field).Reference(x => x.IndicatorField).Load();
                    indicator.InputFields[field.IndicatorField.CodeName!] = field;
                }
            }

            var ga = new GA();
            var mask = new StrategyMask(strat);

            var firstParent = mask.GetParams(strat);

            var currentGeneration = ga.GenerateInitGeneration(firstParent);

            var loader = new TestDataLoader(strat);
            var candles = loader.GetData();

            candles.RemoveAt(0);
            var idx = 0;
            for (var i = 0; i < candles.Count; i++)
            {
                candles[i].TimeStamp = candles[i].TimeStamp.AddMinutes(-1);
            }

            var picker = new IndicatorsPicker();
            var generationCount = 1;
            double bestResult = 0;
            int countBestRepeat = 0;
            while (true)
            {
                logger.Warn("Generation: " + generationCount + " BestResult: " + bestResult);
                var i = 0;
                var threadList = new Task[currentGeneration.Count];
                while (i < currentGeneration.Count)
                {
                    while (i < currentGeneration.Count && hashs.ContainsKey(GetHash(currentGeneration[i])))
                    {
                        threadList[i] = Task.CompletedTask;
                        i++;
                    }
                    if (i >= currentGeneration.Count)
                        break;
                    lock (hashs)
                    {
                        hashs[GetHash(currentGeneration[i])] = 0;
                    }
                    int r = i;
                    var task = new Task(() => {
                        var myHash = GetHash(currentGeneration[r]);
                        var strat1 = mask.GetStrategy(currentGeneration[r]);
                        var res = picker.SilentPickIndicators(strat1, candles, 45);
                        lock(hashs)
                        {
                            hashs[myHash] = res;
                        }
                    });
                    task.Start();
                    Thread.Sleep(10);
                    threadList[i] = task;
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

                var resList = new List<GA.GAPriority>();
                double newBest = 0;
                for (var j = 0; j < currentGeneration.Count; j++)
                {
                    var hash = GetHash(currentGeneration[j]);
                    newBest = Math.Max(hashs[hash], newBest);
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