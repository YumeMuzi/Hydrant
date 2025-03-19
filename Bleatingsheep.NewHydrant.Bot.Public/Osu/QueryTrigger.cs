﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Bleatingsheep.NewHydrant.Attributions;
using Bleatingsheep.NewHydrant.Core;
using Bleatingsheep.Osu;
using Bleatingsheep.Osu.ApiClient;
using Bleatingsheep.OsuQqBot.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PuppeteerSharp;
using Sisters.WudiLib;
using Sisters.WudiLib.Posts;
using Message = Sisters.WudiLib.SendingMessage;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace Bleatingsheep.NewHydrant.Osu
{
    [Component("query")]
    public class QueryTrigger : Service, IMessageCommand
    {
        private static readonly Regex s_selfRegex = new Regex(@"^\s*(?<trigger>[~～∼])\s*[,，]?\s*(?<mode>\S*)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex s_whereRegex = new Regex($@"^\s*(?<trigger>where)\s*(?<name>{OsuHelper.UsernamePattern})\s*[,，]?\s*(?<mode>\S*)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex s_whereQQRegex = new Regex(@"^\s*(?<trigger>where)\s*qq\s*=\s*(?<qq>\d+)\s*[,，]?\s*(?<mode>\S*)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex s_queryPrefixRegex = new Regex(@"^\s*查\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex s_querySuffixRegex = new Regex(@"^\s*[,，]?\s*(?<mode>\S*)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const string SendingFailureCacheKey = nameof(QueryTrigger) + "__risk_control__";
        private const int Threshold = 2;

        private readonly Lazy<IOsuApiClient> _osuApiLazy;
        private readonly Lazy<NewbieContext> _newbieContext;
        private readonly Lazy<QueryHelper> _queryHelper;
        private readonly IMemoryCache _memoryCache;

        private IOsuApiClient OsuApi => _osuApiLazy.Value;
        private NewbieContext NewbieContext => _newbieContext.Value;
        private QueryHelper QueryHelper => _queryHelper.Value;

        #region Parameters

        [Parameter("trigger")]
        public string Trigger { get; set; }

        [Parameter("name")]
        public string Name { get; set; }

        [Parameter("mode")]
        public string ModeString { get; set; }

        [Parameter("qq")]
        public long QQId { get; set; }

        #endregion

        public QueryTrigger(Lazy<IOsuApiClient> osuApiLazy, Lazy<NewbieContext> newbieContext, Lazy<QueryHelper> queryHelper, IMemoryCache memoryCache)
        {
            _osuApiLazy = osuApiLazy;
            _newbieContext = newbieContext;
            _queryHelper = queryHelper;
            _memoryCache = memoryCache;
        }

        private static readonly ImmutableArray<string> s_cqCodeSeq = new[] { "text", "at" }.ToImmutableArray();
        private static readonly ImmutableArray<string> s_cqCodeSeq2 = new[] { "text", "at", "text" }.ToImmutableArray();

        public bool ShouldResponse(MessageContext context)
        {
            if (context.Content.TryGetPlainText(out string text))
            {
                if (RegexCommand(s_selfRegex, text))
                {
                    QQId = context.UserId;
                    return true;
                }
                return RegexCommand(s_whereRegex, text) || RegexCommand(s_whereQQRegex, text);
            }
            else if (context.Content.Sections.Select(f => f.Type).SequenceEqual(s_cqCodeSeq) &&
                RegexCommand(s_queryPrefixRegex, context.Content.Sections[0].Data["text"]))
            {
                QQId = long.Parse(context.Content.Sections[1].Data["qq"]);
                return true;
            }
            else if (context.Content.Sections.Select(f => f.Type).SequenceEqual(s_cqCodeSeq2) &&
                RegexCommand(s_queryPrefixRegex, context.Content.Sections[0].Data["text"]) &&
                RegexCommand(s_querySuffixRegex, context.Content.Sections[2].Data["text"]))
            {
                QQId = long.Parse(context.Content.Sections[1].Data["qq"]);
                return true;
            }
            return false;
        }

        public async Task ProcessAsync(MessageContext context, HttpApiClient api)
        {
            Mode? mode = default(Mode);
            try
            {
                if (!string.IsNullOrWhiteSpace(ModeString))
                {
                    mode = ModeExtensions.Parse(ModeString);
                }
            }
            catch
            {
                // Ignore when the mode string is invalid.
                return;
            }
            Message message;
            if (QQId != default)
            {
                var bindingInfo = await NewbieContext.Bindings.Where(b => b.UserId == QQId).FirstOrDefaultAsync().ConfigureAwait(false);
                if (bindingInfo is null)
                {
                    await api.SendMessageAsync(context.Endpoint, "未绑定 osu! 账号。").ConfigureAwait(false);
                    return;
                }
                var osuId = bindingInfo.OsuId;
                message = await QueryHelper.QueryByUserId(osuId, mode).ConfigureAwait(false);
            }
            else if (!string.IsNullOrEmpty(Name))
            {
                message = await QueryHelper.QueryByUserName(Name, mode).ConfigureAwait(false);
            }
            else
            {
                return;
            }

            //// 临时广告：新人群 post 功能。
            //long[] newbieGroups = [231094840, 928936255, 281624271, 758120648, 514661057]; // Span<long> 要到 9.0 才支持，因为这是个 async 方法。
            //if (DateTimeOffset.UtcNow < new DateTimeOffset(2025, 4, 3, 0, 0, 0, default) && context is GroupMessage g && newbieGroups.Contains(g.GroupId))
            //{
            //    var textWithAdd = message.Sections[0].Data["text"] + "\r\n/post复活: https://osuxrq.com/misc/meme/";
            //    message = new Message(textWithAdd);
            //}

            object sendResponse = null;
            if (!_memoryCache.TryGetValue(SendingFailureCacheKey, out int t) || t < Threshold)
                sendResponse = await api.SendMessageAsync(context.Endpoint, message).ConfigureAwait(false);
            if (sendResponse is null)
            {
                // 可能会假失败，即消息发出去了，但检测到失败。
                //await api.SendMessageAsync(context.Endpoint, $"检测到发送失败，消息长度为{message.Raw.Length}，[调试]将转换成 base64 发送。").ConfigureAwait(false);
                //await api.SendMessageAsync(context.Endpoint, Convert.ToBase64String(Encoding.UTF8.GetBytes(message.Raw))).ConfigureAwait(false);

                // 记录发送失败次数。72小时内失败2次即转图。
                // 不是完全线程安全。
                t = _memoryCache.Set(SendingFailureCacheKey, t + 1, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3),
                    Priority = CacheItemPriority.NeverRemove,
                    SlidingExpiration = TimeSpan.FromDays(1),
                });


                var text = message.Sections[0].Data["text"];
                using var sr = new System.IO.StringReader(text);
                var lines = new List<string>();
                while (sr.ReadLine() is string line)
                {
                    // 分行发送发送失败的消息
                    //await api.SendMessageAsync(context.Endpoint, line).ConfigureAwait(false);

                    lines.Add(line);
                }
                //lines.Add(string.Empty);
                //lines.Add("// 有会 html/css 的帮我弄好看点吗？ 斜杠可怜");

                using var page = await Chrome.OpenNewPageAsync().ConfigureAwait(false);
                await page.SetViewportAsync(new ViewPortOptions
                {
                    DeviceScaleFactor = 3,
                    Width = 360,
                    Height = 202,
                }).ConfigureAwait(false);
                var style = "font-family: 'Source Han Sans CN', sans-serif;";
                await page.SetContentAsync($@"<div lang=""zh-CN"" style=""{style}"">{string.Join("<br>", lines.Select(HttpUtility.HtmlEncode))}</div>").ConfigureAwait(false);
                var data = await page.ScreenshotDataAsync(new ScreenshotOptions
                {
                    FullPage = true,
                }).ConfigureAwait(false);

                await api.SendMessageAsync(context.Endpoint, Message.ByteArrayImage(data)).ConfigureAwait(false);
            }
        }
    }
}
