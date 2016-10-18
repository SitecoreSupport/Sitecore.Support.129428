using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Ninject;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Linq.Utilities;
using Sitecore.ContentSearch.Security;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.SecurityModel;
using Sitecore.Social.Configuration;
using Sitecore.Social.Configuration.Model;
using Sitecore.Social.Domain.Model;
using Sitecore.Social.Infrastructure;
using Sitecore.Social.Infrastructure.Extensions;
using Sitecore.Social.Infrastructure.Identifiers;
using Sitecore.Social.Infrastructure.Logging;
using Sitecore.Social.Infrastructure.Utils;
using Sitecore.Social.Search;
using Sitecore.Social.SitecoreAccess;
using Sitecore.Social.SitecoreAccess.Extensions;

namespace Sitecore.Support.Social.Search
{
    public class SearchProvider : ISearchProvider
    {
        public SearchProvider()
        {
            this.ConfigurationFactory = ExecutingContext.Current.IoC.Get<IConfigurationFactory>();
            this.SearchIndex = this.GetSearchIndex();
        }

        protected IConfigurationFactory ConfigurationFactory { get; set; }

        protected ISearchIndex SearchIndex { get; private set; }

        public IEnumerable<Identifier> GetMessagesByContainer(string container)
        {
            var normalizedContainer = SearchUtil.NormalizeContainer(container, CurrentDatabase.Database.Name);

            Expression<Func<SearchItem, bool>> containerExpression = searchItem => searchItem.Container == normalizedContainer;

            return this.SearchItems(
                containerExpression.And(this.TemplateIsPostingConfigurationExpression()),
                searchItem => searchItem.Parent.GetIdentifier()).ToList();
        }

        public IEnumerable<Identifier> GetMessagesByAccount(Identifier accountId)
        {
            var normalizedAccountId = accountId.GetID();

            Expression<Func<SearchItem, bool>> accountExpression = searchItem => searchItem.AccountId == normalizedAccountId;

            return this.SearchItems(
                accountExpression.And(this.TemplateIsPostingConfigurationExpression()),
                searchItem => searchItem.Parent.GetIdentifier()).ToList();
        }

        public IEnumerable<Identifier> GetMessagesByWorkflowState(Identifier workflowStateId)
        {
            var normalizedWorkflowId = workflowStateId.IsEmpty ? ID.Null : workflowStateId.GetID();

            Expression<Func<SearchItem, bool>> workflowStateExpression = searchItem => searchItem.WorkflowStateId == normalizedWorkflowId;

            return this.SearchItems(workflowStateExpression.And(this.TemplateIsMessageExpression())).ToList();
        }

        public IEnumerable<Identifier> GetPostedMessages()
        {
            Expression<Func<SearchItem, bool>> postedDateNotEmptyExpression = searchItem => searchItem.MessagePostedDate != DateTime.MinValue.Date;

            return this.SearchItems(postedDateNotEmptyExpression.And(this.TemplateIsMessageExpression())).ToList();
        }

        public IEnumerable<Identifier> GetNotPostedMessages()
        {
            Expression<Func<SearchItem, bool>> postedDateEmptyExpression = searchItem => searchItem.MessagePostedDate == DateTime.MinValue.Date;

            return this.SearchItems(postedDateEmptyExpression.And(this.TemplateIsMessageExpression())).ToList();
        }

        public IEnumerable<Identifier> GetPostedMessages(DateTime postedDate)
        {
            Expression<Func<SearchItem, bool>> postedDateExpression = searchItem => searchItem.MessagePostedDate == postedDate.Date;

            return this.SearchItems(postedDateExpression.And(this.TemplateIsMessageExpression())).ToList();
        }

        public IEnumerable<Identifier> GetMessagesByCreatedDate(DateTime createdDate)
        {
            Expression<Func<SearchItem, bool>> createdDateExpression = searchItem => searchItem.MessageCreatedDate == createdDate.Date;

            return this.SearchItems(createdDateExpression.And(this.TemplateIsMessageExpression())).ToList();
        }

        public IEnumerable<Identifier> GetMessagesReadyToPostAutomatically(IEnumerable<Identifier> accountIds)
        {
            Expression<Func<SearchItem, bool>> messageFilterExpression = searchItem =>
                (searchItem.MessagePostedDate == DateTime.MinValue.Date) &&
                (searchItem.FinalWorkflowState || (searchItem.WorkflowStateId == ID.Null));

            var messagesByMessageFilter = this.SearchItems(messageFilterExpression.And(this.TemplateIsMessageExpression())).ToList();

            if (!messagesByMessageFilter.Any())
            {
                return Enumerable.Empty<Identifier>();
            }

            var contentPostingConfigurationTemplateId = this.ConfigurationFactory
                .Get<PostingConfigurationsConfiguration>()
                .PostingConfigurations
                .Where(postingConfiguration => string.Equals(postingConfiguration.Name, "ContentPosting", StringComparison.Ordinal))
                .Select(postingConfiguration => ID.Parse(postingConfiguration.TemplateId))
                .First();

            Expression<Func<SearchItem, bool>> postingConfigurationFilterExpression = searchItem =>
                (searchItem.TemplateId == contentPostingConfigurationTemplateId) &&
                searchItem.PostAutomatically && searchItem.ItemPublished;

            Expression<Func<SearchItem, bool>> accountFilterExpression = null;

            foreach (var accountId in accountIds)
            {
                var normalizedAccountId = accountId.GetID();

                if (accountFilterExpression == null)
                {
                    accountFilterExpression = searchItem => searchItem.AccountId == normalizedAccountId;
                }
                else
                {
                    accountFilterExpression = accountFilterExpression.Or(searchItem => searchItem.AccountId == normalizedAccountId);
                }
            }

            if (accountFilterExpression != null)
            {
                postingConfigurationFilterExpression = postingConfigurationFilterExpression.And(accountFilterExpression);
            }

            var messagesByPostingConfigurationFilter = this.SearchItems(postingConfigurationFilterExpression, searchItem => searchItem.Parent.GetIdentifier()).ToList();

            return !messagesByPostingConfigurationFilter.Any()
                ? Enumerable.Empty<Identifier>() :
                messagesByMessageFilter.Intersect(messagesByPostingConfigurationFilter);
        }

        public void UpdateSearchIndex(Identifier itemId)
        {
            if (this.SearchIndex == null)
            {
                return;
            }

            this.SearchIndex.Update((SitecoreItemUniqueId)new ItemUri(itemId.GetID(), CurrentDatabase.Database), IndexingOptions.ForcedIndexing);
        }

        public void UpdateSearchIndexOnMessage(Identifier messageItemId)
        {
            var postingConfigurationItem = CurrentDatabase.Database.GetItem(messageItemId.GetID())
                .Children.FirstOrDefault(childItem => childItem.IsItemOfType(this.ConfigurationFactory.Get<SettingsConfiguration>().PostingConfigurationBaseTemplateId));

            this.UpdateSearchIndexOnMessage(messageItemId, postingConfigurationItem == null ? IDIdentifier.Empty : postingConfigurationItem.ID.GetIdentifier());
        }

        public void UpdateSearchIndexOnMessage(Identifier messageItemId, Identifier postingConfigurationItemId)
        {
            this.UpdateSearchIndex(messageItemId);

            if (!postingConfigurationItemId.IsEmpty)
            {
                this.UpdateSearchIndex(postingConfigurationItemId);
            }
        }

        protected IEnumerable<Identifier> SearchItems(Expression<Func<SearchItem, bool>> whereExpression)
        {
            return this.SearchItems(whereExpression, searchItem => searchItem.ItemId.GetIdentifier());
        }

        protected IEnumerable<Identifier> SearchItems(Expression<Func<SearchItem, bool>> whereExpression, Func<SearchItem, Identifier> selector)
        {
            if (this.SearchIndex == null)
            {
                return new List<Identifier>();
            }

            using (var searchContext = this.SearchIndex.CreateSearchContext(SearchSecurityOptions.DisableSecurityCheck))
            {
                return searchContext.GetQueryable<SearchItem>()
                    .Where(whereExpression)
                    .ToList()
                    .Select(selector);
            }
        }

        protected bool IndexesAreInitilized()
        {
            return ContentSearchManager.SearchConfiguration.Indexes.Count > 0;
        }

        private ISearchIndex GetSearchIndex()
        {
            if (!this.IndexesAreInitilized())
            {
                Log.SingleWarn("SUPPORT Attempt to access indexes at moment when they were not initialized...", this);
                return null;
            }

            var settingsConfiguration = this.ConfigurationFactory.Get<SettingsConfiguration>();

            ISearchIndex socialIndex = null;
            try
            {
                Item messagesRoot;

                using (new SecurityDisabler())
                {
                    messagesRoot = CurrentDatabase.Database.GetItem(settingsConfiguration.MessagesRootPath);
                }

                if (messagesRoot == null)
                {
                    ExecutingContext.Current.IoC.Get<ILogManager>().LogMessage(
                        string.Format(
                            "Social messages index could not be determined for {0} database. Messages root path item could not be found: {1}.",
                            CurrentDatabase.Database.Name,
                            settingsConfiguration.MessagesRootPath),
                        LogLevel.Error,
                        this);

                    return null;
                }

                socialIndex = ContentSearchManager.GetIndex((SitecoreIndexableItem)messagesRoot);

                if (socialIndex.Name == settingsConfiguration.MessagesSearchIndexMaster || socialIndex.Name == settingsConfiguration.MessagesSearchIndexWeb)
                {
                    return socialIndex;
                }

                socialIndex = ContentSearchManager.Indexes.Single(index => index.Crawlers.Any(crawler =>
                {
                    var sitecoreItemCrawler = crawler as SitecoreItemCrawler;
                    if (sitecoreItemCrawler == null)
                    {
                        return false;
                    }

                    return sitecoreItemCrawler.Database == CurrentDatabase.Database.Name && (index.Name == settingsConfiguration.MessagesSearchIndexMaster || index.Name == settingsConfiguration.MessagesSearchIndexWeb);
                }));
            }
            catch (Exception exception)
            {
                ExecutingContext.Current.IoC.Get<ILogManager>().LogMessage(
                    string.Format(
                        "Social messages index could not be determined for {0} database. Messages root path: {1}. Please check indexes configuration.",
                        CurrentDatabase.Database.Name,
                        settingsConfiguration.MessagesRootPath),
                    LogLevel.Error,
                    this,
                    exception);
            }

            return socialIndex;
        }

        private Expression<Func<SearchItem, bool>> TemplateIsMessageExpression()
        {
            return this.OrTemplateExpression(
                this.ConfigurationFactory
                    .Get<NetworksConfiguration>()
                    .Networks
                    .SelectMany(networkSettings => networkSettings.Items.Select(messageSettings => ID.Parse(messageSettings.MessageTemplateId))));
        }

        private Expression<Func<SearchItem, bool>> TemplateIsPostingConfigurationExpression()
        {
            return this.OrTemplateExpression(
                this.ConfigurationFactory
                    .Get<PostingConfigurationsConfiguration>()
                    .PostingConfigurations
                    .Select(postingConfigurationSettings => ID.Parse(postingConfigurationSettings.TemplateId)));
        }

        private Expression<Func<SearchItem, bool>> OrTemplateExpression(IEnumerable<ID> templateIds)
        {
            var templateIdList = templateIds as IList<ID> ?? templateIds.ToList();

            if (!templateIdList.Any())
            {
                return item => false;
            }

            Expression<Func<SearchItem, bool>> whereExpression = null;
            foreach (var templateId in templateIdList)
            {
                var filterTemplateId = templateId;

                if (whereExpression == null)
                {
                    whereExpression = searchItem => searchItem.TemplateId == filterTemplateId;
                }
                else
                {
                    whereExpression = whereExpression.Or(searchItem => searchItem.TemplateId == filterTemplateId);
                }
            }

            return whereExpression;
        }
    }
}