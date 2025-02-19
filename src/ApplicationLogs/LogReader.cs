using Neo.IO;
using Neo.IO.Data.LevelDB;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.IO.Path;

namespace Neo.Plugins
{
    public class LogReader : Plugin, IPersistencePlugin
    {
        private readonly DB db;

        public override string Name => "ApplicationLogs";

        public override string Description => "Synchronizes the smart contract log with the NativeContract log (Notify)";

        public LogReader()
        {
            db = DB.Open(GetFullPath(Settings.Default.Path), new Options { CreateIfMissing = true });
            RpcServerPlugin.RegisterMethods(this);
        }

        protected override void Configure()
        {
            Settings.Load(GetConfiguration());
        }

        [RpcMethod]
        public JObject GetApplicationLog(JArray _params)
        {
            UInt256 hash = UInt256.Parse(_params[0].AsString());
            byte[] value = db.Get(ReadOptions.Default, hash.ToArray());
            if (value is null)
                throw new RpcException(-100, "Unknown transaction/blockhash");

            var raw = JObject.Parse(Neo.Utility.StrictUTF8.GetString(value));
            //Additional optional "trigger" parameter to getapplicationlog for clients to be able to get just one execution result for a block.
            if (_params.Count >= 2 && Enum.TryParse(_params[1].AsString(), true, out TriggerType trigger))
            {
                var executions = raw["executions"] as JArray;
                for (int i = 0; i < executions.Count;)
                {
                    if (!executions[i]["trigger"].AsString().Equals(trigger.ToString(), StringComparison.OrdinalIgnoreCase))
                        executions.RemoveAt(i);
                    else
                        i++;
                }
            }
            return raw;
        }

        public static JObject TxLogToJson(Blockchain.ApplicationExecuted appExec)
        {
            global::System.Diagnostics.Debug.Assert(appExec.Transaction != null);

            var txJson = new JObject();
            txJson["txid"] = appExec.Transaction.Hash.ToString();
            JObject trigger = new JObject();
            trigger["trigger"] = appExec.Trigger;
            trigger["vmstate"] = appExec.VMState;
            trigger["exception"] = GetExceptionMessage(appExec.Exception);
            trigger["gasconsumed"] = new BigDecimal(appExec.GasConsumed, NativeContract.GAS.Decimals).ToString();
            try
            {
                trigger["stack"] = appExec.Stack.Select(q => q.ToJson()).ToArray();
            }
            catch (InvalidOperationException)
            {
                trigger["stack"] = "error: recursive reference";
            }
            trigger["notifications"] = appExec.Notifications.Select(q =>
            {
                JObject notification = new JObject();
                notification["contract"] = q.ScriptHash.ToString();
                notification["eventname"] = q.EventName;
                try
                {
                    notification["state"] = q.State.ToJson();
                }
                catch (InvalidOperationException)
                {
                    notification["state"] = "error: recursive reference";
                }
                return notification;
            }).ToArray();

            txJson["executions"] = new List<JObject>() { trigger }.ToArray();
            return txJson;
        }

        public static JObject BlockLogToJson(Block block, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
        {
            var blocks = applicationExecutedList.Where(p => p.Transaction is null).ToArray();
            if (blocks.Length > 0)
            {
                var blockJson = new JObject();
                var blockHash = block.Hash.ToArray();
                blockJson["blockhash"] = block.Hash.ToString();
                var triggerList = new List<JObject>();
                foreach (var appExec in blocks)
                {
                    JObject trigger = new JObject();
                    trigger["trigger"] = appExec.Trigger;
                    trigger["vmstate"] = appExec.VMState;
                    trigger["gasconsumed"] = new BigDecimal(appExec.GasConsumed, NativeContract.GAS.Decimals).ToString();
                    try
                    {
                        trigger["stack"] = appExec.Stack.Select(q => q.ToJson()).ToArray();
                    }
                    catch (InvalidOperationException)
                    {
                        trigger["stack"] = "error: recursive reference";
                    }
                    trigger["notifications"] = appExec.Notifications.Select(q =>
                    {
                        JObject notification = new JObject();
                        notification["contract"] = q.ScriptHash.ToString();
                        notification["eventname"] = q.EventName;
                        try
                        {
                            notification["state"] = q.State.ToJson();
                        }
                        catch (InvalidOperationException)
                        {
                            notification["state"] = "error: recursive reference";
                        }
                        return notification;
                    }).ToArray();
                    triggerList.Add(trigger);
                }
                blockJson["executions"] = triggerList.ToArray();
                return blockJson;
            }

            return null;
        }

        void IPersistencePlugin.OnPersist(Block block, StoreView snapshot, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
        {
            WriteBatch writeBatch = new WriteBatch();

            //processing log for transactions
            foreach (var appExec in applicationExecutedList.Where(p => p.Transaction != null))
            {
                var txJson = TxLogToJson(appExec);
                writeBatch.Put(appExec.Transaction.Hash.ToArray(), Neo.Utility.StrictUTF8.GetBytes(txJson.ToString()));
            }

            //processing log for block
            var blockJson = BlockLogToJson(block, applicationExecutedList);
            if (blockJson != null)
            {
                writeBatch.Put(block.Hash.ToArray(), Neo.Utility.StrictUTF8.GetBytes(blockJson.ToString()));
            }
            db.Write(WriteOptions.Default, writeBatch);
        }

        static string GetExceptionMessage(Exception exception)
        {
            return exception?.GetBaseException().Message;
        }
    }
}
