﻿using Foole.Mpq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Heroes.ReplayParser.MPQFiles;
using MpqHeader = Heroes.ReplayParser.MPQFiles.MpqHeader;

namespace Heroes.ReplayParser
{
    public class DataParser
    {
        public enum ReplayParseResult
        {
            Success = 0,
            ComputerPlayerFound = 1,
            Incomplete = 2,
            Duplicate = 3,
            // ChatGlitch = 4, - Past issue that is no longer applicable
            TryMeMode = 5,
            UnexpectedResult = 9,
            Exception = 10,
            FileNotFound = 11,
            // AutoSelectBug = 12, - Past issue that is no longer applicable
            PreAlphaWipe = 13,
            FileSizeTooLarge = 14,
            PTRRegion = 15,
            RetryForBanError = 16,
        }

        public static readonly Dictionary<string, (double, double, double, double)> MapOffsets = new Dictionary<string, (double, double, double, double)>
        {
            { "Garden of Terror", (-2.5, 21.5, 1.05, 0.95) },
            { "Cursed Hollow", (6.0, 35.0, 0.99, 0.906) },
            { "Dragon Shire", (0.0, 32.0, 1.035, 0.941) },
            { "Blackheart's Bay", (-3.0, 12.0, 1.03, 0.935) },
            { "Sky Temple", (-0.25, 22.5, 1.04, 0.942) },
            // { "Haunted Mines", (-3.0, 6.0, 1.027, 0.930) }, - Old 'Haunted Mines' map
            { "Tomb of the Spider Queen", (-4.5, 28.0, 1.075, 0.973) },
            { "Infernal Shrines", (6.5, 41.0, 1.0, 0.92) },
            { "Towers of Doom", (4.0, 42.0, 1.03, 0.925) },
            { "Battlefield of Eternity", (-5.0, 33.0, 1.09, 0.96) },
            { "Braxis Holdout", (-5.0, 33.0, 1.09, 0.96) },
            /* TODO: The following 4 maps have numbers which are simply copied from other
             *       rows, and they have to be validated.
             */
            { "Alterac Pass", (6.5, 41.0, 1.0, 0.92) },
            { "Volskaya Foundry", (6.5, 41.0, 1.0, 0.92) },
            { "Hanamura Temple", (-5.0, 33.0, 1.09, 0.96) },
            { "Warhead Junction", (6.0, 35.0, 0.99, 0.906) },
            //
        };

        public static Tuple<ReplayParseResult, Replay> ParseReplay(byte[] bytes, bool ignoreErrors = false, bool allowPTRRegion = false)
        {
            return ParseReplay(bytes, new ParseOptions
            {
                AllowPTR = allowPTRRegion,
                IgnoreErrors = ignoreErrors,
            });
        }

        public static Tuple<ReplayParseResult, Replay> ParseReplay(
            string fileName,
            bool ignoreErrors,
            bool deleteFile,
            bool allowPTRRegion = false,
            bool detailedBattleLobbyParsing = false)
        {
            return ParseReplay(fileName, deleteFile, new ParseOptions
            {
                AllowPTR = allowPTRRegion,
                IgnoreErrors = ignoreErrors,
                ShouldParseDetailedBattleLobby = detailedBattleLobbyParsing,
            });
        }

        public static Tuple<ReplayParseResult, Replay> ParseReplay(byte[] bytes, ParseOptions parseOptions)
        {
            try
            {
                var replay = new Replay();

                // File in the version numbers for later use.
                MpqHeader.ParseHeader(replay, bytes);

                if (!parseOptions.IgnoreErrors && replay.ReplayBuild < 32455)
                    return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.PreAlphaWipe, null);
                using (var memoryStream = new MemoryStream(bytes))
                using (var archive = new MpqArchive(memoryStream))
                    ParseReplayArchive(replay, archive, parseOptions);

                return ParseReplayResults(replay, parseOptions.IgnoreErrors, parseOptions.AllowPTR);
            }
            catch
            {
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.Exception, null);
            }
        }
        public static Tuple<ReplayParseResult, Replay> ParseReplay(string fileName, bool deleteFile, ParseOptions parseOptions)
        {
            try
            {
                var replay = new Replay();

                // File in the version numbers for later use.
                MpqHeader.ParseHeader(replay, fileName);

                if (!parseOptions.IgnoreErrors && replay.ReplayBuild < 32455)
                    return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.PreAlphaWipe, null);

                using (var archive = new MpqArchive(fileName))
                    ParseReplayArchive(replay, archive, parseOptions);

                if (deleteFile)
                    File.Delete(fileName);

                return ParseReplayResults(replay, parseOptions.IgnoreErrors, parseOptions.AllowPTR);
            }
            catch
            {
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.Exception, null);
            }
        }

        private static Tuple<ReplayParseResult, Replay> ParseReplayResults(Replay replay, bool ignoreErrors, bool allowPTRRegion)
        {
            if (ignoreErrors)
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.UnexpectedResult, replay);
            else if (replay.Players.Length == 1)
                // Filter out 'Try Me' games, as they have unusual format that throws exceptions in other areas
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.TryMeMode, null);
            else if (replay.Players.Length <= 5)
                // Custom game with all computer players on the opposing team won't register them as players at all (Noticed at build 34053)
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.ComputerPlayerFound, null);
            else if (replay.Players.All(i => !i.IsWinner) || replay.ReplayLength.TotalMinutes < 2)
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.Incomplete, null);
            else if (replay.Timestamp == DateTime.MinValue)
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.UnexpectedResult, null);
            else if (replay.Timestamp < new DateTime(2014, 10, 6, 0, 0, 0, DateTimeKind.Utc))
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.PreAlphaWipe, null);
            else if (replay.Players.Count(i => i.PlayerType == PlayerType.Computer || i.Character == "Random Hero" || i.Name.Contains(' ')) > (replay.GameMode == GameMode.Brawl ? 5 : 0))
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.ComputerPlayerFound, null);
            else if (!allowPTRRegion && replay.Players.Any(i => i.BattleNetRegionId >= 90 /* PTR/Test Region */))
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.PTRRegion, null);
            else if (replay.Players.Count(i => i.IsWinner) != 5 || replay.Players.Length != 10 || (replay.GameMode != GameMode.StormLeague && 
                    replay.GameMode != GameMode.TeamLeague && 
                    replay.GameMode != GameMode.HeroLeague && 
                    replay.GameMode != GameMode.UnrankedDraft && 
                    replay.GameMode != GameMode.QuickMatch && 
                    replay.GameMode != GameMode.Custom && 
                    replay.GameMode != GameMode.Brawl &&
                    replay.GameMode != GameMode.ARAM))
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.UnexpectedResult, null);
            else
                return new Tuple<ReplayParseResult, Replay>(ReplayParseResult.Success, replay);
        }

        private static void ParseReplayArchive(Replay replay, MpqArchive archive, ParseOptions parseOptions)
        {
            archive.AddListfileFilenames();

            // Replay Details
            ReplayDetails.Parse(replay, GetMpqFile(archive, ReplayDetails.FileName), parseOptions.IgnoreErrors);

            if (!parseOptions.IgnoreErrors)
            {
                if (replay.Players.Length != 10 || replay.Players.Count(i => i.IsWinner) != 5)
                    // Filter out 'Try Me' games, any games without 10 players, and incomplete games
                    return;
                else if (replay.Timestamp == DateTime.MinValue)
                    // Uncommon issue when parsing replay.details
                    return;
                else if (replay.Timestamp < new DateTime(2014, 10, 6, 0, 0, 0, DateTimeKind.Utc))
                    // Technical Alpha replays
                    return;
            }

            // Replay Init Data
            ReplayInitData.Parse(replay, GetMpqFile(archive, ReplayInitData.FileName));

            ReplayAttributeEvents.Parse(replay, GetMpqFile(archive, ReplayAttributeEvents.FileName));

            if (parseOptions.ShouldParseEvents)
            {
                replay.TrackerEvents = ReplayTrackerEvents.Parse(GetMpqFile(archive, ReplayTrackerEvents.FileName));
                try
                {
                    replay.GameEvents = ReplayGameEvents.Parse(GetMpqFile(archive, ReplayGameEvents.FileName), replay.ClientListByUserID, replay.ReplayBuild, replay.ReplayVersionMajor, parseOptions.ShouldParseMouseEvents);
                    replay.IsGameEventsParsedSuccessfully = true;
                }
                catch
                {
                    replay.GameEvents = new List<GameEvent>();

                }

                {
                    // Gather talent selections
                    var talentGameEventsDictionary = replay.GameEvents
                        .Where(i => i.eventType == GameEventType.CHeroTalentSelectedEvent)
                        .GroupBy(i => i.player)
                        .ToDictionary(
                            i => i.Key,
                            i => i.Select(j => new Talent { TalentID = (int)j.data.unsignedInt.Value, TimeSpanSelected = j.TimeSpan }).OrderBy(j => j.TimeSpanSelected).ToArray());

                    foreach (var player in talentGameEventsDictionary.Keys)
                        player.Talents = talentGameEventsDictionary[player];
                }
                // Replay Server Battlelobby
                if (archive.Any(i => i.Filename == ReplayServerBattlelobby.FileName))
                {
                    if (parseOptions.ShouldParseDetailedBattleLobby)
                        ReplayServerBattlelobby.Parse(replay, GetMpqFile(archive, ReplayServerBattlelobby.FileName));
                    else
                        ReplayServerBattlelobby.GetBattleTags(replay, GetMpqFile(archive, ReplayServerBattlelobby.FileName));
                }

                // Parse Unit Data using Tracker events
                if (parseOptions.ShouldParseUnits)
                {
                    Unit.ParseUnitData(replay);
                }

                // Parse Statistics
                if (parseOptions.ShouldParseStatistics)
                {
                    if (replay.ReplayBuild >= 40431)
                        try
                        {
                            Statistics.Parse(replay);
                            replay.IsStatisticsParsedSuccessfully = true;
                        }
                        catch
                        {
                            replay.IsGameEventsParsedSuccessfully = false;
                        }
                }

                // Replay Message Events
                if (parseOptions.ShouldParseMessageEvents)
                {
                    ReplayMessageEvents.Parse(replay, GetMpqFile(archive, ReplayMessageEvents.FileName));
                }

                // Replay Resumable Events
                // So far it doesn't look like this file has anything we would be interested in
                // ReplayResumableEvents.Parse(replay, GetMpqFile(archive, "replay.resumable.events"));
            }
        }

        public static byte[] GetMpqFile(MpqArchive archive, string fileName)
        {
            using (var mpqStream = archive.OpenFile(archive.Single(i => i.Filename == fileName)))
            {
                var buffer = new byte[mpqStream.Length];
                mpqStream.Read(buffer, 0, buffer.Length);
                return buffer;
            }
        }

        public static bool VerifyReplayMessageEventCleared(string fileName)
        {
            using (var archive = new MpqArchive(fileName))
            {
                archive.AddListfileFilenames();
                return GetMpqFile(archive, "replay.message.events").Length == 1;
            }
        }
    }
}
