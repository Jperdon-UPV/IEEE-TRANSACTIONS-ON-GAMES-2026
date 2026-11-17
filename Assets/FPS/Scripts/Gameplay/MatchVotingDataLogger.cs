using System.Collections.Generic;
using System.IO;
using Unity.FPS.AI;
using Unity.Netcode;
using UnityEngine;
using Unity.FPS.Core;
using Unity.Collections;

namespace Unity.FPS.Gameplay
{
    [System.Serializable]
    public class VoteEntry
    {
        public string timestamp;
        public string voterName;
        public string targetName;
        public string votedAs; // "Human" or "Bot"
    }

    [System.Serializable]
    public class TargetVotingSummary
    {
        public string targetName;
        public string trueIdentity; // "Human" or "Bot"
        public int votesAsHumanCount;
        public int votesAsBotCount;
    }

    [System.Serializable]
    public class MatchInformation
    {
        public string matchStartTime;
        public string matchEndTime = "In Progress...";
        public string matchWinner = "Undecided";
        public int maxPlayers;
        public List<string> redTeam = new List<string>();
        public List<string> blueTeam = new List<string>();
    }

    [System.Serializable]
    public class MatchVotingRecord
    {
        public MatchInformation matchInformation = new MatchInformation();
        public List<TargetVotingSummary> votingSummary = new List<TargetVotingSummary>();
        public List<VoteEntry> votingTimeline = new List<VoteEntry>();
    }

    public class MatchVotingDataLogger : MonoBehaviour
    {
        public static MatchVotingDataLogger Instance { get; private set; }

        private MatchVotingRecord m_CurrentMatchRecord = new MatchVotingRecord();
        private Dictionary<string, string> m_CachedIdentities = new Dictionary<string, string>();

        private string m_CurrentSaveFilePath = string.Empty;
        private bool m_IsRecordFinalized = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private System.Collections.IEnumerator Start()
        {
            yield return new WaitUntil(() => NetworkManager.Singleton != null);

            if (NetworkManager.Singleton.IsServer)
            {
                yield return new WaitUntil(() => GameScoreManager.Instance != null);
                GameScoreManager.Instance.OnMatchEnded += HandleMatchEndedCleanly;
                Debug.Log("[LOGGER] Attached to GameScoreManager.");
            }
        }

        private void OnDestroy()
        {
            if (GameScoreManager.Instance != null)
            {
                GameScoreManager.Instance.OnMatchEnded -= HandleMatchEndedCleanly;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnDisable()
        {
            HandlePrematureTermination();
        }

        private void OnApplicationQuit()
        {
            HandlePrematureTermination();
        }

        public void RecordMatchStart(Unity.Services.Lobbies.Models.Lobby activeLobby)
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;

            m_IsRecordFinalized = false;
            m_CurrentMatchRecord = new MatchVotingRecord();
            m_CachedIdentities.Clear();

            m_CurrentMatchRecord.matchInformation.matchStartTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            if (activeLobby != null)
            {
                m_CurrentMatchRecord.matchInformation.maxPlayers = activeLobby.MaxPlayers;
                m_CurrentMatchRecord.matchInformation.redTeam.Clear();
                m_CurrentMatchRecord.matchInformation.blueTeam.Clear();
                m_CurrentMatchRecord.votingSummary.Clear(); // Clear any old summary data

                foreach (var lobbyPlayer in activeLobby.Players)
                {
                    if (lobbyPlayer.Data != null && lobbyPlayer.Data.ContainsKey("PlayerTeam") && lobbyPlayer.Data.ContainsKey("PlayerName"))
                    {
                        string team = lobbyPlayer.Data["PlayerTeam"].Value;
                        string pName = lobbyPlayer.Data["PlayerName"].Value;
                        string prefabChoice = lobbyPlayer.Data.ContainsKey("PlayerPrefab") ? lobbyPlayer.Data["PlayerPrefab"].Value : "HumanPlayer";
                        string formattedPlayerEntry = $"{pName} ({prefabChoice})";

                        string trueIdentity = (prefabChoice.Contains("Bot") || prefabChoice.Contains("AI")) ? "Bot" : "Human";
                        if (!m_CachedIdentities.ContainsKey(pName))
                        {
                            m_CachedIdentities.Add(pName, trueIdentity);
                        }

                        if (team == "Red") m_CurrentMatchRecord.matchInformation.redTeam.Add(formattedPlayerEntry);
                        else if (team == "Blue") m_CurrentMatchRecord.matchInformation.blueTeam.Add(formattedPlayerEntry);

                        // INITIALIZATION: Populate everyone into the summary with 0 votes at start
                        TargetVotingSummary initialSummary = new TargetVotingSummary
                        {
                            targetName = pName,
                            trueIdentity = trueIdentity,
                            votesAsHumanCount = 0,
                            votesAsBotCount = 0
                        };
                        m_CurrentMatchRecord.votingSummary.Add(initialSummary);
                    }
                }
            }

            string folderPath = Path.Combine(Application.dataPath, "../", "VotingRecords");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string fileTimestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"MatchVotingRecord_{fileTimestamp}.json";
            m_CurrentSaveFilePath = Path.Combine(folderPath, fileName);

            SaveRecordToDisk();
            Debug.Log($"[LOGGER] Live logging initialized at: {m_CurrentSaveFilePath}");
        }

        public void RecordVote(string voter, string target, string guessType)
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;
            if (m_IsRecordFinalized || string.IsNullOrEmpty(m_CurrentSaveFilePath)) return;

            // 1. Log to the timeline
            VoteEntry newVote = new VoteEntry
            {
                timestamp = System.DateTime.Now.ToString("HH:mm:ss"),
                voterName = voter,
                targetName = target,
                votedAs = guessType
            };
            m_CurrentMatchRecord.votingTimeline.Add(newVote);

            // 2. LIVE INCREMENT: Find the target in the votingSummary and increment right now
            TargetVotingSummary targetSummary = m_CurrentMatchRecord.votingSummary.Find(s => s.targetName == target);

            // Safety check: if target wasn't initialized in lobby data, dynamically add them now
            if (targetSummary == null)
            {
                string trueIdentity = m_CachedIdentities.ContainsKey(target) ? m_CachedIdentities[target] : "Unknown";
                targetSummary = new TargetVotingSummary
                {
                    targetName = target,
                    trueIdentity = trueIdentity,
                    votesAsHumanCount = 0,
                    votesAsBotCount = 0
                };
                m_CurrentMatchRecord.votingSummary.Add(targetSummary);
            }

            // Increment based on the current raw vote type submitted
            if (guessType == "Human")
            {
                targetSummary.votesAsHumanCount++;
            }
            else if (guessType == "Bot")
            {
                targetSummary.votesAsBotCount++;
            }

            SaveRecordToDisk();
        }

        private void HandleMatchEndedCleanly(string winner)
        {
            FinalizeMatchRecord(winner);
        }

        private void HandlePrematureTermination()
        {
            if (m_IsRecordFinalized || string.IsNullOrEmpty(m_CurrentSaveFilePath)) return;

            Debug.LogWarning("[LOGGER] Match interrupted prematurely! Finalizing with failure states.");
            FinalizeMatchRecord("Match ended prematurely - No Winner");
        }

        private void FinalizeMatchRecord(string finalWinnerOutcome)
        {
            if (m_IsRecordFinalized) return;
            m_IsRecordFinalized = true;

            m_CurrentMatchRecord.matchInformation.matchEndTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            m_CurrentMatchRecord.matchInformation.matchWinner = finalWinnerOutcome;

            SaveRecordToDisk();

            Debug.Log($"[LOGGER] Match file finalized successfully. Outcome: {finalWinnerOutcome}");
        }

        private void SaveRecordToDisk()
        {
            try
            {
                if (string.IsNullOrEmpty(m_CurrentSaveFilePath)) return;
                string jsonOutput = JsonUtility.ToJson(m_CurrentMatchRecord, true);
                File.WriteAllText(m_CurrentSaveFilePath, jsonOutput);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LOGGER] Failed writing live logging step: {e.Message}");
            }
        }
    }
}