/*
****************************************************************************
*  Copyright (c) 2024,  Skyline Communications NV  All Rights Reserved.    *
****************************************************************************

By using this script, you expressly agree with the usage terms and
conditions set out below.
This script and all related materials are protected by copyrights and
other intellectual property rights that exclusively belong
to Skyline Communications.

A user license granted for this script is strictly for personal use only.
This script may not be used in any way by anyone without the prior
written consent of Skyline Communications. Any sublicensing of this
script is forbidden.

Any modifications to this script by the user are only allowed for
personal use and within the intended purpose of the script,
and will remain the sole responsibility of the user.
Skyline Communications will not be responsible for any damages or
malfunctions whatsoever of the script resulting from a modification
or adaptation by the user.

The content of this script is confidential information.
The user hereby agrees to keep this confidential information strictly
secret and confidential and not to disclose or reveal it, in whole
or in part, directly or indirectly to any person, entity, organization
or administration without the prior written consent of
Skyline Communications.

Any inquiries can be addressed to:

	Skyline Communications NV
	Ambachtenstraat 33
	B-8870 Izegem
	Belgium
	Tel.	: +32 51 31 35 69
	Fax.	: +32 51 31 01 29
	E-mail	: info@skyline.be
	Web		: www.skyline.be
	Contact	: Ben Vandenberghe

****************************************************************************
Revision History:

DATE		VERSION		AUTHOR			COMMENTS

28/03/2024	1.0.0.1		RDM, Skyline	Initial version
****************************************************************************
*/

namespace SLC_AS_sFlowManagerAddFilterExpression_1
{
	using System;
	using System.Collections.Generic;
    using System.Linq;
    using System.Globalization;
    using System.Text;
    using Skyline.DataMiner.Automation;
    using Messages;
    using Skyline.DataMiner.Core.DataMinerSystem.Automation;
    using Newtonsoft.Json;

    /// <summary>
    /// Represents a DataMiner Automation script.
    /// </summary>
    public class Script
	{
        /// <summary>
        /// The script entry point.
        /// </summary>
        /// <param name="engine">Link with SLAutomation process.</param>
        public void Run(Engine engine)
        {
            try
            {
                engine.Timeout = TimeSpan.FromMinutes(30);
                engine.SetFlag(RunTimeFlags.NoKeyCaching);

                var addFilter = new AddFilterQuery(engine);
                addFilter.Run();
            }
            catch (InteractiveUserDetachedException)
            {
                engine.ExitSuccess("User detached");
            }
            catch (Exception e)
            {
                engine.ExitFail(String.Format("Adding filter failed: {0}", e.Message));
            }
        }
        public class AddFilterQuery
        {
            private const int ExternalRequestPID = 2;
            private const int AgentsTablePID = 1000;
            private const int AgentsIPTableColumnIDX = 0;
            private const int AgentsNameTableColumnIDX = 2;
            private const int AgentsFilterQueryTableColumnWritePID = 1104;
            private const int FiltersTablePID = 2300;

            private Engine engine;
            private IActionableElement sFlowManager;
            private Dictionary<string, Agent> agents;
            private List<string> AvailableFilters;

            private string name;
            private string description;
            private bool assignToAgents = false;
            private string errorMessage;
            private List<FilterItem> filterQueryItems = new List<FilterItem>();

            public AddFilterQuery(Engine engine)
            {
                this.engine = engine;

                sFlowManager = engine.GetDummy("sFlow Manager");

                InitAgents();
                InitAvailableFilters();

                errorMessage = "";
            }

            public void Run()
            {
                var action = Actions.BuildQuery;
                while (action != Actions.Finished)
                {
                    action = Execute(action);
                    //do something extra
                }
            }

            private Actions Execute(Actions action)
            {
                switch (action)
                {
                    case Actions.SelectAgents:
                        return SelectAgents();
                    case Actions.BuildQuery:
                        return BuildQuery();
                    case Actions.Update:
                        return Update();
                    case Actions.Assign:
                        return Assign();
                }

                return Actions.Finished;
            }

            private Actions BuildQuery()
            {
                var uir = new UIResults();
                var uib = new UIBuilder();
                uib.Height = 575;
                uib.RequireResponse = true;

                var row = 0;

                uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.StaticText, Text = "Name", Row = row, Column = 0, Width = 150 });
                uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.TextBox, InitialValue = name, DestVar = "name", Row = row, Column = 1, Width = 200, ColumnSpan = 2 });

                uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.StaticText, Text = "Description", Row = ++row, Column = 0, Width = 150 });
                uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.TextBox, InitialValue = description, DestVar = "description", Row = row, Column = 1, Width = 200, Height = 100, ColumnSpan = 2, IsMultiline = true });

                uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.StaticText, Text = "BUILD QUERY", Row = ++row, Column = 0, Width = 150, Style = "Title2", ColumnSpan = 2 });

                if (AvailableFilters.Count == 0)
                {
                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.StaticText, Text = "No available filters!", Row = ++row, Column = 0, Width = 300 });

                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.Button, Text = "Cancel", DestVar = "cancel", Row = ++row, Column = 0, Width = 100 });
                }
                else
                {
                    if (filterQueryItems.Count == 0)
                    {
                        var filtersDropdown = new UIBlockDefinition { Type = UIBlockType.DropDown, InitialValue = "Select Filter", DestVar = "nextFilter", WantsOnChange = true, Row = ++row, Column = 1, Width = 200 };
                        filtersDropdown.AddDropDownOption("Select Filter");
                        filtersDropdown.AddDropDownOption("-------------");
                        foreach (var filter in AvailableFilters.OrderBy(f => f))
                            filtersDropdown.AddDropDownOption(filter);
                        uib.AppendBlock(filtersDropdown);
                    }
                    else
                    {
                        var unclosedParenthesisCount = 0;
                        for (int i = 0; i < filterQueryItems.Count; i++)
                        {
                            var filterItem = filterQueryItems[i];

                            var leadingConditionDropdown = new UIBlockDefinition { Type = UIBlockType.DropDown, InitialValue = filterItem.LeadingCondition, DestVar = String.Format("leadingCondition{0}", i), WantsOnChange = true, Row = ++row, Column = 0, Width = 100 };
                            leadingConditionDropdown.AddDropDownOption("");
                            if (i == 0)
                            {
                                leadingConditionDropdown.AddDropDownOption("NOT");
                                leadingConditionDropdown.AddDropDownOption("(");
                                leadingConditionDropdown.AddDropDownOption("( NOT");
                            }
                            else
                            {
                                leadingConditionDropdown.AddDropDownOption("AND");
                                leadingConditionDropdown.AddDropDownOption("AND NOT");
                                leadingConditionDropdown.AddDropDownOption("AND (");
                                leadingConditionDropdown.AddDropDownOption("AND NOT (");
                                leadingConditionDropdown.AddDropDownOption("OR");
                                leadingConditionDropdown.AddDropDownOption("OR NOT");
                                leadingConditionDropdown.AddDropDownOption("OR (");
                                leadingConditionDropdown.AddDropDownOption("OR NOT (");
                            }
                            uib.AppendBlock(leadingConditionDropdown);

                            if (!String.IsNullOrEmpty(filterItem.LeadingCondition) && filterItem.LeadingCondition.Contains("("))
                                unclosedParenthesisCount++;

                            var filterDropdown = new UIBlockDefinition { Type = UIBlockType.DropDown, InitialValue = filterItem.Filter, DestVar = String.Format("filter{0}", i), Row = row, Column = 1, Width = 200 };
                            foreach (var filter in AvailableFilters.OrderBy(f => f))
                                filterDropdown.AddDropDownOption(filter);
                            uib.AppendBlock(filterDropdown);

                            if (unclosedParenthesisCount > 0)
                            {
                                var trailingConditionDropdown = new UIBlockDefinition { Type = UIBlockType.DropDown, InitialValue = filterItem.TrailingCondition, DestVar = String.Format("trailingCondition{0}", i), WantsOnChange = true, Row = row, Column = 2, Width = 100 };
                                trailingConditionDropdown.AddDropDownOption("");
                                for (int j = 1; j <= unclosedParenthesisCount; j++)
                                    trailingConditionDropdown.AddDropDownOption(new string(')', j));

                                uib.AppendBlock(trailingConditionDropdown);
                            }

                            if (!String.IsNullOrEmpty(filterItem.TrailingCondition) && filterItem.TrailingCondition.Contains(")"))
                                unclosedParenthesisCount--;
                        }

                        var filtersDropdown = new UIBlockDefinition { Type = UIBlockType.DropDown, InitialValue = "Select Filter", DestVar = "nextFilter", WantsOnChange = true, Row = ++row, Column = 1, Width = 200 };
                        filtersDropdown.AddDropDownOption("Select Filter");
                        filtersDropdown.AddDropDownOption("-------------");
                        foreach (var filter in AvailableFilters.OrderBy(f => f))
                            filtersDropdown.AddDropDownOption(filter);
                        uib.AppendBlock(filtersDropdown);
                    }

                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.CheckBox, Text = "Assign To Agents", InitialValue = assignToAgents.ToString(), DestVar = "assignToAgents", Row = ++row, Column = 1, Width = 200, VerticalAlignment = "Center" });

                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.Button, Text = "Update", DestVar = "update", Row = ++row, Column = 1, Width = 100 });
                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.Button, Text = "Cancel", DestVar = "cancel", Row = ++row, Column = 1, Width = 100 });

                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.StaticText, Text = errorMessage, Row = ++row, Column = 1, Width = 300, ColumnSpan = 3 });
                }

                uib.ColumnDefs = "a;a;a";
                uib.RowDefs = String.Join(";", Enumerable.Repeat("a", row + 1));
                uir = engine.ShowUI(uib);

                name = uir.GetString("name");
                description = uir.GetString("description");
                assignToAgents = uir.GetChecked("assignToAgents");
                if (uir.WasOnChange("nextFilter"))
                {
                    var nextFilter = uir.GetString("nextFilter");
                    if (nextFilter != "Select Filter" && nextFilter != "-------------")
                    {
                        var filterItem = new FilterItem { LeadingCondition = "", Filter = nextFilter, TrailingCondition = "" };
                        filterQueryItems.Add(filterItem);
                    }
                }
                else
                {
                    for (int i = 0; i < filterQueryItems.Count; i++)
                    {
                        var filter = filterQueryItems[i];
                        filter.LeadingCondition = uir.GetString(String.Format("leadingCondition{0}", i));
                        filter.Filter = uir.GetString(String.Format("filter{0}", i));
                        filter.TrailingCondition = uir.GetString(String.Format("trailingCondition{0}", i));
                    }
                }

                if (uir.WasButtonPressed("cancel"))
                    return Actions.Finished;
                if (uir.WasButtonPressed("update"))
                {
                    if (IsFilterQueryValid())
                    {
                        if (!String.IsNullOrEmpty(name) && !String.IsNullOrEmpty(description))
                        {
                            if (assignToAgents)
                            {
                                engine.ShowProgress("Updating filter expression...");
                                Update();

                                return Actions.SelectAgents;
                            }
                            else
                                return Actions.Update;
                        }
                        else
                            errorMessage = "Name and Description can't be empty!";
                    }
                }

                return Actions.BuildQuery;
            }
            private Actions SelectAgents()
            {
                var uir = new UIResults();
                var uib = new UIBuilder();
                uib.Height = 575;
                uib.RequireResponse = true;

                var row = 0;

                uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.StaticText, Text = "SELECT AGENTS", Row = row, Column = 0, Width = 150, Style = "Title2" });

                if (agents.Count == 0)
                {
                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.StaticText, Text = "No available agents!", Row = ++row, Column = 0, Width = 300 });

                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.Button, Text = "Cancel", DestVar = "cancel", Row = ++row, Column = 0, Width = 100 });
                }
                else
                {
                    var agentsCheckBoxList = new UIBlockDefinition { Type = UIBlockType.CheckBoxList, DestVar = "selectedAgents", Row = ++row, Column = 0, Height = 250, Width = 300 };
                    foreach (var agent in agents.Values.OrderBy(a => a.DisplayName))
                        agentsCheckBoxList.AddCheckBoxListOption(agent.IP, agent.DisplayName);
                    uib.AppendBlock(agentsCheckBoxList);

                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.Button, Text = "Update", DestVar = "update", Row = ++row, Column = 0, Width = 100 });
                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.Button, Text = "Cancel", DestVar = "cancel", Row = ++row, Column = 0, Width = 100 });

                    uib.AppendBlock(new UIBlockDefinition { Type = UIBlockType.StaticText, Text = errorMessage, Row = ++row, Column = 0, Width = 300 });
                }

                uib.ColumnDefs = "a";
                uib.RowDefs = String.Join(";", Enumerable.Repeat("a", row + 1));
                uir = engine.ShowUI(uib);

                var selectedAgents = uir.GetString("selectedAgents").Split(';');
                foreach (var agent in agents.Values)
                {
                    if (selectedAgents.Contains(agent.IP))
                        agent.IsSelected = true;
                    else
                        agent.IsSelected = false;
                }

                if (uir.WasButtonPressed("cancel"))
                    return Actions.Finished;
                if (uir.WasButtonPressed("update"))
                {
                    if (!agents.Values.Any(a => a.IsSelected))
                        errorMessage = "No agents selected!";
                    else
                        return Actions.Assign;
                }

                return Actions.SelectAgents;
            }
            private Actions Update()
            {
                StringBuilder query = new StringBuilder();
                foreach (var filterQueryItem in filterQueryItems)
                {
                    if (!String.IsNullOrEmpty(filterQueryItem.LeadingCondition))
                    {
                        query.Append(filterQueryItem.LeadingCondition);

                        if (!filterQueryItem.LeadingCondition.EndsWith("("))
                            query.Append(" ");
                    }

                    query.Append(filterQueryItem.Filter);

                    if (!String.IsNullOrEmpty(filterQueryItem.TrailingCondition))
                        query.Append(filterQueryItem.TrailingCondition);

                    query.Append(" ");
                }

                sFlowManager.SetParameter(ExternalRequestPID, JsonConvert.SerializeObject(new FilterExpressionUpdateMessage
                {
                    Name = name,
                    Description = description,
                    Query = query.ToString(),
                }));

                return Actions.Finished;
            }

            private Actions Assign()
            {
                foreach (var agent in agents.Values.Where(a => a.IsSelected))
                    sFlowManager.SetParameterByPrimaryKey(AgentsFilterQueryTableColumnWritePID, agent.IP, name);

                return Actions.Finished;
            }

            private void InitAgents()
            {
                agents = new Dictionary<string, Agent>();

                var agentColumns = sFlowManager.GetColumns(engine, AgentsTablePID, new int[] { AgentsIPTableColumnIDX, AgentsNameTableColumnIDX });
                if (agentColumns != null && agentColumns.Length == 2)
                {
                    var agentIPs = (object[])agentColumns[0];
                    var agentNames = (object[])agentColumns[1];
                    for (int i = 0; i < agentIPs.Length; i++)
                    {
                        var agent = new Agent
                        {
                            IP = (string)agentIPs[i],
                            Name = (string)agentNames[i],
                            IsSelected = false
                        };
                        agents[agent.IP] = agent;
                    }
                }
            }

            private void InitAvailableFilters()
            {
                AvailableFilters = sFlowManager.GetTablePrimaryKeys(FiltersTablePID).ToList();
            }

            private bool IsFilterQueryValid()
            {
                if (filterQueryItems.Count == 0)
                {
                    errorMessage = "No filter selected!";
                    return false;
                }

                var numberOfOpenParenthesis = 0;
                var numberOfClosedParenthesis = 0;
                for (int i = 0; i < filterQueryItems.Count; i++)
                {
                    var filterQueryItem = filterQueryItems[i];

                    if (i > 0 && String.IsNullOrEmpty(filterQueryItem.LeadingCondition))
                    {
                        errorMessage = "Operator not selected for all filters!";
                        return false;
                    }

                    if (!String.IsNullOrEmpty(filterQueryItem.LeadingCondition))
                        numberOfOpenParenthesis += filterQueryItem.LeadingCondition.Count(c => c == '(');
                    if (!String.IsNullOrEmpty(filterQueryItem.TrailingCondition))
                        numberOfClosedParenthesis += filterQueryItem.TrailingCondition.Count(c => c == ')');
                }

                if (numberOfOpenParenthesis != numberOfClosedParenthesis)
                {
                    errorMessage = "Parenthesis are not correctly closed!";
                    return false;
                }

                return true;
            }

            private enum Actions { SelectAgents, BuildQuery, Update, Assign, Finished }

            private class Agent
            {
                public string IP { get; set; }
                public string Name { get; set; }
                public string DisplayName { get { return String.Format("{0} ({1})", IP, Name); } }
                public bool IsSelected { get; set; }
            }

            private class FilterItem
            {
                public string LeadingCondition { get; set; }
                public string Filter { get; set; }
                public string TrailingCondition { get; set; }
            }
        }
    }

    public static class ElementExtensions
    {
        /// <summary>
        /// Returns columns from table
        /// Needs reference to C:\Skyline DataMiner\Files\Interop.SLDms.dll in Automation editor
        /// </summary>
        /// <param name="element">Element</param>
        /// <param name="tablePid">PID of table</param>
        /// <param name="columnIdxs">Column idxs of the columns that need to be returned</param>
        /// <returns>object array of columns</returns>
        /*public static object[] GetColumns(this IActionableElement element, IEngine engine, int tablePid, int[] columnIdxs)
        {
            var dms = engine.GetDms();
            var ids = new uint[] { (uint)element.DmaId, (uint)element.ElementId };

            var returnValue = new object();
            dms.Notify(87, 0, ids, tablePid, out returnValue);

            var table = (object[])returnValue;
            if (table != null && table.Length > 4)
            {
                var columns = (object[])table[4];
                if (columns != null && columns.Length > columnIdxs.Max())
                {
                    var rowCount = ((object[])columns[0]).Length;

                    var returnColumns = new object[columnIdxs.Length];
                    for (int i = 0; i < columnIdxs.Length; i++)
                        returnColumns[i] = new object[rowCount];

                    for (int i = 0; i < columnIdxs.Length; i++)
                    {
                        var column = (object[])columns[columnIdxs[i]];
                        for (int j = 0; j < rowCount; j++)
                        {
                            var cell = (object[])column[j];
                            if (cell != null && cell.Length > 0)
                                ((object[])returnColumns[i])[j] = cell[0];
                        }
                    }

                    return returnColumns;
                }
            }

            return null;
        }*/

        /// <summary>
        /// Returns columns from table
        /// Needs reference to C:\Skyline DataMiner\Files\Interop.SLDms.dll in Automation editor
        /// </summary>
        /// <param name="element">Element</param>
        /// <param name="tablePid">PID of table</param>
        /// <param name="columnIdxs">Column idxs of the columns that need to be returned</param>
        /// <returns>object array of columns</returns>
        public static object[] GetColumns(this IActionableElement element, IEngine engine, int tablePid, int[] columnIdxs)
        {
            var dms = engine.GetDms();
            var newElement = dms.GetElement(element.DmaId + "/" + element.ElementId);
            var table = newElement.GetTable(tablePid);
            List<object> columns = new List<object>();
            foreach (var idx in columnIdxs)
            {
                columns.Add(table.GetColumn<object>(idx));
            }

            return columns.ToArray();
        }
    }
}

namespace Messages
{
    public class FilterExpressionUpdateMessage
    {
        public string Command = "FilterExpressionUpdateMessage";

        public string Name { get; set; }

        public string Description { get; set; }

        public string Query { get; set; }
    }
}