using System;
using System.Numerics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Miningcore.Blockchain.Koto.DaemonResponses;
using Miningcore.Blockchain.Koto.Configuration;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Stratum;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace Miningcore.Blockchain.Koto
{
    public class KotoJob
    {
        public KotoBlockTemplate BlockTemplate { get; private set; }
        public PoolConfig PoolConfig { get; private set; }
        public string PreviousBlockHash { get; private set; }
        public string CoinbaseTransaction { get; private set; }
        public string[] Transactions { get; private set; }
        public string MerkleRoot { get; private set; }
        public string Bits { get; private set; }
        public string Time { get; private set; }
        public string Nonce { get; private set; }
        public double Difficulty { get; set; }
        public string JobId { get; set; }
        protected KotoCoinTemplate coin;
        protected Network network;
        private readonly ConcurrentDictionary<string, bool> submits = new(StringComparer.OrdinalIgnoreCase);

public KotoJob(string id, KotoBlockTemplate blockTemplate, PoolConfig poolConfig) : base(id)
{
    JobId = id;
    BlockTemplate = blockTemplate;
    coin = poolConfig.Template.As<KotoCoinTemplate>();
    networkParams = coin.GetNetwork(network.ChainName);
    Difficulty = (double)new BigRational(networkParams.Diff1BValue, BlockTemplate.Target.HexToReverseByteArray().AsSpan().ToBigInteger());
    PoolConfig = poolConfig;
    PreviousBlockHash = blockTemplate.PreviousBlockHash;
    CoinbaseTransaction = CreateCoinbaseTransaction();
    Transactions = blockTemplate.Transactions;
    MerkleRoot = CalculateMerkleRoot();
    Bits = blockTemplate.Bits;
}

        private string CreateCoinbaseTransaction()
        {
            var extraNoncePlaceholder = new byte[4]; // Placeholder for extraNonce

            var p1 = SerializeCoinbasePart1(extraNoncePlaceholder);
            var p2 = SerializeCoinbasePart2();

            return p1.ToString();//Combine(p1, extraNoncePlaceholder, p2);
        }

        private byte[] SerializeCoinbasePart1(byte[] extraNoncePlaceholder)
        {
            var txVersion = 1;
            var txInputsCount = 1;
            var txOutputsCount = 1;
            var txLockTime = 0;
            var txInPrevOutHash = new byte[32]; // "0" in hex
            var txInPrevOutIndex = BitConverter.GetBytes(uint.MaxValue);
            var txInSequence = BitConverter.GetBytes(uint.MaxValue);

            var scriptSigPart1 = Combine(
                SerializeNumber((long)BlockTemplate.Height),
                SerializeNumber(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                new[] { (byte)extraNoncePlaceholder.Length }
            );

            var nVersionGroupId = GetVersionGroupId(txVersion);

            var p1 = Combine(
                BitConverter.GetBytes(txVersion),
                nVersionGroupId,
                new byte[0], // txTimestamp for POS coins
                VarIntBuffer((ulong)txInputsCount),
                txInPrevOutHash,
                txInPrevOutIndex,
                VarIntBuffer((ulong)(scriptSigPart1.Length + extraNoncePlaceholder.Length)),
                scriptSigPart1
            );

            return p1;
        }

        private byte[] SerializeCoinbasePart2()
        {
            var txInSequence = BitConverter.GetBytes(uint.MaxValue);
            var txLockTime = BitConverter.GetBytes(0);
            var outputTransactions = GenerateOutputTransactions();
            var txComment = new byte[0]; // For coins that support/require transaction comments

            var nExpiryHeight = GetExpiryHeight(BlockTemplate.Version);
            var valueBalance = GetValueBalance(BlockTemplate.Version);
            var vShieldedSpend = GetShieldedSpend(BlockTemplate.Version);
            var vShieldedOutput = GetShieldedOutput(BlockTemplate.Version);
            var nJoinSplit = GetJoinSplit(BlockTemplate.Version);

            var p2 = Combine(
                SerializeString(GetBlockIdentifier()),
                txInSequence,
                outputTransactions,
                txLockTime,
                nExpiryHeight,
                valueBalance,
                vShieldedSpend,
                vShieldedOutput,
                nJoinSplit,
                txComment
            );

            return p2;
        }

        private byte[] GetVersionGroupId(int txVersion)
        {
            if ((txVersion & 0x7fffffff) == 3)
                return BitConverter.GetBytes(0x2e7d970);
            if ((txVersion & 0x7fffffff) == 4)
                return BitConverter.GetBytes(0x9023e50a);
            return new byte[0];
        }

        private byte[] GetExpiryHeight(int txVersion)
        {
            return (txVersion & 0x7fffffff) >= 3 ? BitConverter.GetBytes(0) : new byte[0];
        }

        private byte[] GetValueBalance(int txVersion)
        {
            return (txVersion & 0x7fffffff) >= 4 ? BitConverter.GetBytes(0L) : new byte[0];
        }

        private byte[] GetShieldedSpend(int txVersion)
        {
            return (txVersion & 0x7fffffff) >= 4 ? new byte[] { 0 } : new byte[0];
        }

        private byte[] GetShieldedOutput(int txVersion)
        {
            return (txVersion & 0x7fffffff) >= 4 ? new byte[] { 0 } : new byte[0];
        }

        private byte[] GetJoinSplit(int txVersion)
        {
            return (txVersion & 0x7fffffff) >= 2 ? new byte[] { 0 } : new byte[0];
        }

        private byte[] GenerateOutputTransactions()
        {
            // TODO: Implement output transactions generation logic
            return new byte[0];
        }

        private byte[] SerializeNumber(long value)
        {
            return BitConverter.GetBytes(value);
        }

        private string GetBlockIdentifier()
        {
            return "https://github.com/zone117x/node-stratum";
        }

        private string Sha256Hash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        private string ComputeMerkleRoot(List<string> txHashes)
        {
            if (txHashes.Count == 0)
                return string.Empty;

            while (txHashes.Count > 1)
            {
                if (txHashes.Count % 2 != 0)
                    txHashes.Add(txHashes.Last());

                var newLevel = new List<string>();

                for (int i = 0; i < txHashes.Count; i += 2)
                {
                    var left = txHashes[i];
                    var right = txHashes[i + 1];
                    newLevel.Add(Sha256Hash(left + right));
                }

                txHashes = newLevel;
            }

            return txHashes[0];
        }

    public virtual (Share Share, string BlockHex) ProcessShare(StratumConnection worker, string extraNonce2, string nTime, string solution)
        {
            var context = worker.ContextAs<KotoWorkerContext>();
            var extraNonce1 = context.ExtraNonce1;
            var nonce = context.ExtraNonce1 + extraNonce2;
            var submitTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (extraNonce2.Length / 2 != context.ExtraNonce2Size)
                throw new StratumException(StratumError.Other, "incorrect size of extraNonce2");
            if (nTime.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of ntime");
            var nTimeInt = Convert.ToInt64(nTime, 16);
            if (nTimeInt < BlockTemplate.CurTime || nTimeInt > submitTime + 7200)
                throw new StratumException(StratumError.Other, "ntime out of range");

            if (nonce.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of nonce");

            if (!RegisterSubmit(extraNonce1, extraNonce2, nTime, nonce))
                throw new StratumException(StratumError.DuplicateShare, "duplicate share");
            var extraNonce1Buffer = Encoders.Hex.DecodeData(extraNonce1);
            var extraNonce2Buffer = Encoders.Hex.DecodeData(extraNonce2);

            var coinbaseBuffer = SerializeCoinbase(extraNonce1Buffer, extraNonce2Buffer);
            var coinbaseHash = Sha256Hash(coinbaseBuffer.ToString());

            var merkleRoot = ComputeMerkleRoot(new List<string> { coinbaseHash });
            var merkleRootReversed = ReverseBytes(Encoders.Hex.DecodeData(merkleRoot));

            var headerBuffer = SerializeHeader(merkleRootReversed, nTime, nonce);
            var headerHash = Sha256Hash(headerBuffer.ToString());
            BigInteger bigInteger = new BigInteger(headerHash.HexToReverseByteArray());
            if (bigInteger.Sign < 0)
            {
            bigInteger = BigInteger.Negate(bigInteger);
            }
            var headerBigNum = bigInteger;
            BigInteger constantValue = new BigInteger(0x00000000FFFF0000);
            BigInteger headerBigInteger = new BigInteger(headerBigNum.ToByteArrayUnsigned());
            double shareDiff = (double)constantValue / (double)headerBigInteger;
            var blockDiffAdjusted = Difficulty;

            string blockHash = null;
            string blockHex = null;
            if (BlockTemplate.Target.CompareTo(headerBigNum) >= 0)
            {
                blockHex = SerializeBlock(headerBuffer, coinbaseBuffer);
                blockHash = Sha256Hash(headerBuffer.ToString());
            }
            else
            {   
                if (shareDiff / context.Difficulty < 0.99)
                {
                    if (context.PreviousDifficulty.HasValue && shareDiff >= context.PreviousDifficulty)
                    {
                        context.Difficulty = context.PreviousDifficulty.Value;
                    }
                    else
                    {
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
                    }
                }
            }

            var share = new Share
            {
                BlockHeight = (long) BlockTemplate.Height,
                BlockReward = BlockTemplate.CoinbaseValue,
                Difficulty = shareDiff,
                NetworkDifficulty = blockDiffAdjusted,
                BlockHash = blockHash,
                Worker = context.Worker,
                IsBlockCandidate = blockHash != null
            };

            return (share, blockHex);
        }


        private byte[] SerializeCoinbase(byte[] extraNonce1, byte[] extraNonce2)
        {
            var p1 = SerializeCoinbasePart1(extraNonce1);
            var p2 = SerializeCoinbasePart2();

            return Combine(p1, extraNonce1, extraNonce2, p2);
        }

        private byte[] SerializeHeader(byte[] merkleRoot, string nTime, string nonce)
        {
            int headerSize = BlockTemplate.Version == 5 ? 112 : 80;
            var header = new byte[headerSize];
            int position = 0;

            if (BlockTemplate.Version == 5)
            {
                var saplingRoot = Encoders.Hex.DecodeData(BlockTemplate.FinalSaplingRootHash);
                Array.Copy(saplingRoot, 0, header, position, saplingRoot.Length);
                position += saplingRoot.Length;
            }

            var nonceBytes = Encoders.Hex.DecodeData(nonce);
            var nTimeBytes = Encoders.Hex.DecodeData(nTime);
            var bitsBytes = Encoders.Hex.DecodeData(BlockTemplate.Bits);
            var prevHashBytes = Encoders.Hex.DecodeData(BlockTemplate.PreviousBlockHash);
            var versionBytes = BitConverter.GetBytes(BlockTemplate.Version);

            Array.Copy(nonceBytes, 0, header, position, nonceBytes.Length);
            position += nonceBytes.Length;

            Array.Copy(bitsBytes, 0, header, position, bitsBytes.Length);
            position += bitsBytes.Length;

            Array.Copy(nTimeBytes, 0, header, position, nTimeBytes.Length);
            position += nTimeBytes.Length;

            Array.Copy(merkleRoot, 0, header, position, merkleRoot.Length);
            position += merkleRoot.Length;

            Array.Copy(prevHashBytes, 0, header, position, prevHashBytes.Length);
            position += prevHashBytes.Length;

            Array.Copy(versionBytes, 0, header, position, versionBytes.Length);

            Array.Reverse(header);
            Nonce = nonce;
            Time = nTime;
            return header;
        }

        private byte[] SerializeBlock(byte[] header, byte[] coinbase)
        {
            var transactions = BlockTemplate.Transactions.Select(Encoders.Hex.DecodeData).ToList();
            var transactionCount = (ulong)(transactions.Count + 1);

            var block = new List<byte>(header);
            block.AddRange(VarIntBuffer(transactionCount));
            block.AddRange(coinbase);
            transactions.ForEach(tx => block.AddRange(tx));

            // POSコインの場合は0バイトを追加
//            if (PoolConfig.Template.Reward == RewardType.POS)
//            {
//                block.Add(0);
 //           }

            return block.ToArray();
        }

        private bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce)
        {
            var submission = extraNonce1 + extraNonce2 + nTime + nonce;
            return submits.TryAdd(submission, true);
        }

        private byte[] ReverseBytes(byte[] bytes)
        {
            Array.Reverse(bytes);
            return bytes;
        }

        private byte[] Combine(params byte[][] arrays)
        {
            var combined = new byte[arrays.Sum(a => a.Length)];
            int offset = 0;
            foreach (var array in arrays)
            {
                Buffer.BlockCopy(array, 0, combined, offset, array.Length);
                offset += array.Length;
            }
            return combined;
        }

        private byte[] VarIntBuffer(ulong value)
        {
            if (value < 0xfd)
                return new byte[] { (byte)value };

            if (value <= 0xffff)
            {
                var buffer = new byte[3];
                buffer[0] = 0xfd;
                Array.Copy(BitConverter.GetBytes((ushort)value), 0, buffer, 1, 2);
                return buffer;
            }

            if (value <= 0xffffffff)
            {
                var buffer = new byte[5];
                buffer[0] = 0xfe;
                Array.Copy(BitConverter.GetBytes((uint)value), 0, buffer, 1, 4);
                return buffer;
            }

            var buffer64 = new byte[9];
            buffer64[0] = 0xff;
            Array.Copy(BitConverter.GetBytes(value), 0, buffer64, 1, 8);
            return buffer64;
        }

        private byte[] SerializeString(string str)
        {
            var strBytes = Encoding.UTF8.GetBytes(str);
            var length = VarIntBuffer((ulong)strBytes.Length);
            return Combine(length, strBytes);
        }


    }
}
