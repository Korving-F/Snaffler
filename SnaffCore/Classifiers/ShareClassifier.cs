﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SnaffCore.Concurrency;
using SnaffCore.TreeWalk;
using Config = SnaffCore.Config.Config;

namespace Classifiers
{
    public class ShareClassifier
    {
        private ClassifierRule ClassifierRule { get; set; }

        public ShareClassifier(ClassifierRule inRule)
        {
            this.ClassifierRule = inRule;
        }

        public bool ClassifyShare(string share)
        {
            TaskFactory treeWalkerTaskFactory = LimitedConcurrencyLevelTaskScheduler.GetSnafflerTaskFactory();
            CancellationTokenSource treeWalkerCts = LimitedConcurrencyLevelTaskScheduler.GetSnafflerCts();
            BlockingMq Mq = BlockingMq.GetMq();
            Config myConfig = Config.GetConfig();
            // first time we hit sysvol, toggle the flag and keep going. every other time, bail out.
            if (share.ToLower().EndsWith("sysvol"))
            {
                if (myConfig.Options.ScanSysvol == false)
                {
                    return true;
                }
                myConfig.Options.ScanSysvol = false;
            };
            // same for netlogon
            if (share.ToLower().EndsWith("netlogon"))
            {
                if (myConfig.Options.ScanNetlogon == false)
                {
                    return true;
                }
                myConfig.Options.ScanNetlogon = false;
            }
            // check if it matches
            TextClassifier textClassifier = new TextClassifier(ClassifierRule);
            TextResult textResult = textClassifier.TextMatch(share);
            if (textResult != null)
            {
                // if it does, see what we're gonna do with it
                switch (ClassifierRule.MatchAction)
                {
                    case MatchAction.Discard:
                        return true;
                    case MatchAction.Snaffle:
                        // in this context snaffle means 'send a report up the queue but don't scan the share'
                        if (IsShareReadable(share))
                        {
                            ShareResult shareResult = new ShareResult()
                            {
                                Listable = true,
                                SharePath = share
                            };
                            Mq.ShareResult(shareResult);
                        }
                        return true;
                    default:
                        Mq.Error("You've got a misconfigured share ClassifierRule named " + ClassifierRule.RuleName + ".");
                        return false;
                }
            }
            // by default all shares should go on to TreeWalker
            // send them to TreeWalker
            if (IsShareReadable(share))
            {
                ShareResult shareResult = new ShareResult()
                {
                    Listable = true,
                    SharePath = share
                };
                Mq.ShareResult(shareResult);

                Mq.Info("Creating a TreeWalker task for " + shareResult.SharePath);
                var t = treeWalkerTaskFactory.StartNew(() =>
                {
                    try
                    {
                        new TreeWalker(shareResult.SharePath);
                    }
                    catch (Exception e)
                    {
                        Mq.Trace(e.ToString());
                    }
                }, treeWalkerCts.Token);
                return true;
            }

            return false;
        }

        internal bool IsShareReadable(string share)
        {
            BlockingMq Mq = BlockingMq.GetMq();
            try
            {
                string[] files = Directory.GetFiles(share);
                return true;
            }
            catch (Exception e)
            {
                Mq.Trace(e.ToString());
            }
            return false;
        }
    }

    public class ShareResult
    {
        public bool Snaffle { get; set; }
        public bool ScanShare { get; set; }
        public string SharePath { get; set; }
        public bool Listable { get; set; }
        public Triage Triage { get; set; }
    }
}