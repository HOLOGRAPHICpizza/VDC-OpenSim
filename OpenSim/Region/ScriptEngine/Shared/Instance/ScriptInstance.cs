/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Security.Policy;
using System.Reflection;
using System.Globalization;
using System.Xml;
using OpenMetaverse;
using log4net;
using Nini.Config;
using Amib.Threading;
using OpenSim.Framework;
using OpenSim.Region.CoreModules;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.Api.Runtime;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.Shared.CodeTools;
using OpenSim.Region.ScriptEngine.Interfaces;

namespace OpenSim.Region.ScriptEngine.Shared.Instance
{
    public class ScriptInstance : MarshalByRefObject, IScriptInstance
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// The current work item if an event for this script is running or waiting to run,
        /// </summary>
        /// <remarks>
        /// Null if there is no running or waiting to run event.  Must be changed only under an EventQueue lock.
        /// </remarks>
        private IScriptWorkItem m_CurrentWorkItem;

        private IScript m_Script;
        private DetectParams[] m_DetectParams;
        private bool m_TimerQueued;
        private DateTime m_EventStart;
        private bool m_InEvent;
        private string m_Assembly;
        private string m_CurrentEvent = String.Empty;
        private bool m_InSelfDelete;
        private int m_MaxScriptQueue;
        private bool m_SaveState = true;
        private int m_ControlEventsInQueue;
        private int m_LastControlLevel;
        private bool m_CollisionInQueue;

        // The following is for setting a minimum delay between events
        private double m_minEventDelay;
        
        private long m_eventDelayTicks;
        private long m_nextEventTimeTicks;
        private bool m_startOnInit = true;
        private UUID m_AttachedAvatar;
        private StateSource m_stateSource;
        private bool m_postOnRez;
        private bool m_startedFromSavedState;
        private UUID m_CurrentStateHash;
        private UUID m_RegionID;

        public Dictionary<KeyValuePair<int, int>, KeyValuePair<int, int>> LineMap { get; set; }

        private Dictionary<string,IScriptApi> m_Apis = new Dictionary<string,IScriptApi>();

        public Object[] PluginData = new Object[0];

        /// <summary>
        /// Used by llMinEventDelay to suppress events happening any faster than this speed.
        /// This currently restricts all events in one go. Not sure if each event type has
        /// its own check so take the simple route first.
        /// </summary>
        public double MinEventDelay
        {
            get { return m_minEventDelay; }
            set
            {
                if (value > 0.001)
                    m_minEventDelay = value;
                else 
                    m_minEventDelay = 0.0;

                m_eventDelayTicks = (long)(m_minEventDelay * 10000000L);
                m_nextEventTimeTicks = DateTime.Now.Ticks;
            }
        }

        public bool Running { get; set; }

        public bool Suspended
        {
            get { return m_Suspended; }

            set
            {
                // Need to do this inside a lock in order to avoid races with EventProcessor()
                lock (m_Script)
                {
                    bool wasSuspended = m_Suspended;
                    m_Suspended = value;
    
                    if (wasSuspended && !m_Suspended)
                    {
                        lock (EventQueue)
                        {
                            // Need to place ourselves back in a work item if there are events to process
                            if (EventQueue.Count > 0 && Running && !ShuttingDown)
                                m_CurrentWorkItem = Engine.QueueEventHandler(this);
                        }
                    }
                }
            }
        }
        private bool m_Suspended;

        public bool ShuttingDown { get; set; }

        public string State { get; set; }

        public IScriptEngine Engine { get; private set; }

        public UUID AppDomain { get; set; }

        public string PrimName { get; private set; }

        public string ScriptName { get; private set; }

        public UUID ItemID { get; private set; }

        public UUID ObjectID { get; private set; }

        public uint LocalID { get; private set; }

        public UUID RootObjectID { get; private set; }

        public uint RootLocalID { get; private set; }

        public UUID AssetID { get; private set; }

        public Queue EventQueue { get; private set; }

        public int StartParam { get; set; }

        public TaskInventoryItem ScriptTask { get; private set; }

        public DateTime TimeStarted { get; private set; }

        public long MeasurementPeriodTickStart { get; private set; }

        public long MeasurementPeriodExecutionTime { get; private set; }

        public static readonly long MaxMeasurementPeriod = 30 * TimeSpan.TicksPerMinute;

        public void ClearQueue()
        {
            m_TimerQueued = false;
            EventQueue.Clear();
        }

        public ScriptInstance(IScriptEngine engine, SceneObjectPart part,
                UUID itemID, UUID assetID, string assembly,
                AppDomain dom, string primName, string scriptName,
                int startParam, bool postOnRez, StateSource stateSource,
                int maxScriptQueue)
        {
            State = "default";
            EventQueue = new Queue(32);

            Engine = engine;
            LocalID = part.LocalId;
            ObjectID = part.UUID;
            RootLocalID = part.ParentGroup.LocalId;
            RootObjectID = part.ParentGroup.UUID;
            ItemID = itemID;
            AssetID = assetID;
            PrimName = primName;
            ScriptName = scriptName;
            m_Assembly = assembly;
            StartParam = startParam;
            m_MaxScriptQueue = maxScriptQueue;
            m_stateSource = stateSource;
            m_postOnRez = postOnRez;
            m_AttachedAvatar = part.ParentGroup.AttachedAvatar;
            m_RegionID = part.ParentGroup.Scene.RegionInfo.RegionID;

            if (part != null)
            {
                lock (part.TaskInventory)
                {
                    if (part.TaskInventory.ContainsKey(ItemID))
                    {
                        ScriptTask = part.TaskInventory[ItemID];
                    }
                }
            }

            ApiManager am = new ApiManager();

            foreach (string api in am.GetApis())
            {
                m_Apis[api] = am.CreateApi(api);
                m_Apis[api].Initialize(engine, part, LocalID, itemID);
            }
    
            try
            {
                if (dom != System.AppDomain.CurrentDomain)
                    m_Script = (IScript)dom.CreateInstanceAndUnwrap(
                            Path.GetFileNameWithoutExtension(assembly),
                            "SecondLife.Script");
                else
                    m_Script = (IScript)Assembly.Load(
                            Path.GetFileNameWithoutExtension(assembly)).CreateInstance(
                            "SecondLife.Script");

                //ILease lease = (ILease)RemotingServices.GetLifetimeService(m_Script as ScriptBaseClass);
                //RemotingServices.GetLifetimeService(m_Script as ScriptBaseClass);
//                lease.Register(this);
            }
            catch (Exception e)
            {
                m_log.ErrorFormat(
                    "[SCRIPT INSTANCE]: Error loading assembly {0}.  Exception {1}{2}",
                    assembly, e.Message, e.StackTrace);
            }

            try
            {
                foreach (KeyValuePair<string,IScriptApi> kv in m_Apis)
                {
                    m_Script.InitApi(kv.Key, kv.Value);
                }

//                // m_log.Debug("[Script] Script instance created");

                part.SetScriptEvents(ItemID,
                                     (int)m_Script.GetStateEventFlags(State));
            }
            catch (Exception e)
            {
                m_log.ErrorFormat(
                    "[SCRIPT INSTANCE]: Error loading script instance from assembly {0}.  Exception {1}{2}",
                    assembly, e.Message, e.StackTrace);

                return;
            }

            m_SaveState = true;

            string savedState = Path.Combine(Path.GetDirectoryName(assembly),
                    ItemID.ToString() + ".state");
            if (File.Exists(savedState))
            {
                string xml = String.Empty;

                try
                {
                    FileInfo fi = new FileInfo(savedState);
                    int size = (int)fi.Length;
                    if (size < 512000)
                    {
                        using (FileStream fs = File.Open(savedState,
                                                         FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            System.Text.UTF8Encoding enc =
                                new System.Text.UTF8Encoding();

                            Byte[] data = new Byte[size];
                            fs.Read(data, 0, size);

                            xml = enc.GetString(data);

                            ScriptSerializer.Deserialize(xml, this);

                            AsyncCommandManager.CreateFromData(Engine,
                                LocalID, ItemID, ObjectID,
                                PluginData);

//                            m_log.DebugFormat("[Script] Successfully retrieved state for script {0}.{1}", PrimName, m_ScriptName);

                            part.SetScriptEvents(ItemID,
                                    (int)m_Script.GetStateEventFlags(State));

                            Running = false;

                            if (ShuttingDown)
                                m_startOnInit = false;

                            // we get new rez events on sim restart, too
                            // but if there is state, then we fire the change
                            // event

                            // We loaded state, don't force a re-save
                            m_SaveState = false;
                            m_startedFromSavedState = true;
                        }
                    }
                    else
                    {
                        m_log.ErrorFormat(
                            "[SCRIPT INSTANCE]: Unable to load script state from assembly {0}: Memory limit exceeded",
                            assembly);
                    }
                }
                catch (Exception e)
                {
                     m_log.ErrorFormat(
                         "[SCRIPT INSTANCE]: Unable to load script state from assembly {0}.  XML is {1}.  Exception {2}{3}",
                         assembly, xml, e.Message, e.StackTrace);
                }
            }
//            else
//            {
//                ScenePresence presence = Engine.World.GetScenePresence(part.OwnerID);

//                if (presence != null && (!postOnRez))
//                    presence.ControllingClient.SendAgentAlertMessage("Compile successful", false);

//            }
        }

        public void Init()
        {
            if (!m_startOnInit) return;

            if (m_startedFromSavedState) 
            {
                Start();
                if (m_postOnRez) 
                {
                    PostEvent(new EventParams("on_rez",
                        new Object[] {new LSL_Types.LSLInteger(StartParam)}, new DetectParams[0]));
                }

                if (m_stateSource == StateSource.AttachedRez)
                {
                    PostEvent(new EventParams("attach",
                        new object[] { new LSL_Types.LSLString(m_AttachedAvatar.ToString()) }, new DetectParams[0]));
                }
                else if (m_stateSource == StateSource.RegionStart)
                {
                    //m_log.Debug("[Script] Posted changed(CHANGED_REGION_RESTART) to script");
                    PostEvent(new EventParams("changed",
                        new Object[] { new LSL_Types.LSLInteger((int)Changed.REGION_RESTART) }, new DetectParams[0]));
                }
                else if (m_stateSource == StateSource.PrimCrossing || m_stateSource == StateSource.Teleporting)
                {
                    // CHANGED_REGION
                    PostEvent(new EventParams("changed",
                        new Object[] { new LSL_Types.LSLInteger((int)Changed.REGION) }, new DetectParams[0]));

                    // CHANGED_TELEPORT
                    if (m_stateSource == StateSource.Teleporting)
                        PostEvent(new EventParams("changed",
                            new Object[] { new LSL_Types.LSLInteger((int)Changed.TELEPORT) }, new DetectParams[0]));
                }
            }
            else 
            {
                Start();
                PostEvent(new EventParams("state_entry",
                                          new Object[0], new DetectParams[0]));
                if (m_postOnRez) 
                {
                    PostEvent(new EventParams("on_rez",
                        new Object[] {new LSL_Types.LSLInteger(StartParam)}, new DetectParams[0]));
                }

                if (m_stateSource == StateSource.AttachedRez)
                {
                    PostEvent(new EventParams("attach",
                        new object[] { new LSL_Types.LSLString(m_AttachedAvatar.ToString()) }, new DetectParams[0]));
                }

            }
        }

        private void ReleaseControls()
        {
            SceneObjectPart part = Engine.World.GetSceneObjectPart(LocalID);
            
            if (part != null)
            {
                int permsMask;
                UUID permsGranter;
                lock (part.TaskInventory)
                {
                    if (!part.TaskInventory.ContainsKey(ItemID))
                        return;

                    permsGranter = part.TaskInventory[ItemID].PermsGranter;
                    permsMask = part.TaskInventory[ItemID].PermsMask;
                }

                if ((permsMask & ScriptBaseClass.PERMISSION_TAKE_CONTROLS) != 0)
                {
                    ScenePresence presence = Engine.World.GetScenePresence(permsGranter);
                    if (presence != null)
                        presence.UnRegisterControlEventsToScript(LocalID, ItemID);
                }
            }
        }

        public void DestroyScriptInstance()
        {
            ReleaseControls();
            AsyncCommandManager.RemoveScript(Engine, LocalID, ItemID);
        }

        public void RemoveState()
        {
            string savedState = Path.Combine(Path.GetDirectoryName(m_Assembly),
                    ItemID.ToString() + ".state");

            try
            {
                File.Delete(savedState);
            }
            catch(Exception)
            {
            }
        }

        public void VarDump(Dictionary<string, object> vars)
        {
            // m_log.Info("Variable dump for script "+ ItemID.ToString());
            // foreach (KeyValuePair<string, object> v in vars)
            // {
                // m_log.Info("Variable: "+v.Key+" = "+v.Value.ToString());
            // }
        }

        public void Start()
        {
            lock (EventQueue)
            {
                if (Running)
                    return;

                Running = true;

                TimeStarted = DateTime.Now;
                MeasurementPeriodTickStart = Util.EnvironmentTickCount();
                MeasurementPeriodExecutionTime = 0;

                if (EventQueue.Count > 0)
                {
                    if (m_CurrentWorkItem == null)
                        m_CurrentWorkItem = Engine.QueueEventHandler(this);
                    // else
                        // m_log.Error("[Script] Tried to start a script that was already queued");
                }
            }
        }

        public bool Stop(int timeout)
        {
//            m_log.DebugFormat(
//                "[SCRIPT INSTANCE]: Stopping script {0} {1} in {2} {3} with timeout {4} {5} {6}",
//                ScriptName, ItemID, PrimName, ObjectID, timeout, m_InSelfDelete, DateTime.Now.Ticks);

            IScriptWorkItem workItem;

            lock (EventQueue)
            {
                if (!Running)
                    return true;

                // If we're not running or waiting to run an event then we can safely stop.
                if (m_CurrentWorkItem == null)
                {
                    Running = false;
                    return true;
                }

                // If we are waiting to run an event then we can try to cancel it.
                if (m_CurrentWorkItem.Cancel())
                {
                    m_CurrentWorkItem = null;
                    Running = false;
                    return true;
                }

                workItem = m_CurrentWorkItem;
                Running = false;
            }

            // Wait for the current event to complete.
            if (!m_InSelfDelete && workItem.Wait(new TimeSpan((long)timeout * 100000)))
            {
                return true;
            }

            lock (EventQueue)
            {
                workItem = m_CurrentWorkItem;
            }

            if (workItem == null)
                return true;

            // If the event still hasn't stopped and we the stop isn't the result of script or object removal, then
            // forcibly abort the work item (this aborts the underlying thread).
            if (!m_InSelfDelete)
            {
//                m_log.ErrorFormat(
//                    "[SCRIPT INSTANCE]: Aborting script {0} {1} in prim {2} {3} {4} {5}",
//                    ScriptName, ItemID, PrimName, ObjectID, m_InSelfDelete, DateTime.Now.Ticks);

                workItem.Abort();
            }

            lock (EventQueue)
            {
                m_CurrentWorkItem = null;
            }

            return true;
        }

        public void SetState(string state)
        {
            if (state == State)
                return;

            PostEvent(new EventParams("state_exit", new Object[0],
                                       new DetectParams[0]));
            PostEvent(new EventParams("state", new Object[] { state },
                                       new DetectParams[0]));
            PostEvent(new EventParams("state_entry", new Object[0],
                                       new DetectParams[0]));

            throw new EventAbortException();
        }

        /// <summary>
        /// Post an event to this script instance.
        /// </summary>
        /// <remarks>
        /// The request to run the event is sent
        /// </remarks>
        /// <param name="data"></param>
        public void PostEvent(EventParams data)
        {
//            m_log.DebugFormat("[Script] Posted event {2} in state {3} to {0}.{1}",
//                        PrimName, ScriptName, data.EventName, State);

            if (!Running)
                return;

            // If min event delay is set then ignore any events untill the time has expired
            // This currently only allows 1 event of any type in the given time period.
            // This may need extending to allow for a time for each individual event type.
            if (m_eventDelayTicks != 0)
            {
                if (DateTime.Now.Ticks < m_nextEventTimeTicks)
                    return;
                m_nextEventTimeTicks = DateTime.Now.Ticks + m_eventDelayTicks;
            }

            lock (EventQueue)
            {
                if (EventQueue.Count >= m_MaxScriptQueue)
                    return;

                if (data.EventName == "timer")
                {
                    if (m_TimerQueued)
                        return;
                    m_TimerQueued = true;
                }

                if (data.EventName == "control")
                {
                    int held = ((LSL_Types.LSLInteger)data.Params[1]).value;
                    // int changed = ((LSL_Types.LSLInteger)data.Params[2]).value;

                    // If the last message was a 0 (nothing held)
                    // and this one is also nothing held, drop it
                    //
                    if (m_LastControlLevel == held && held == 0)
                        return;

                    // If there is one or more queued, then queue
                    // only changed ones, else queue unconditionally
                    //
                    if (m_ControlEventsInQueue > 0)
                    {
                        if (m_LastControlLevel == held)
                            return;
                    }

                    m_LastControlLevel = held;
                    m_ControlEventsInQueue++;
                }

                if (data.EventName == "collision")
                {
                    if (m_CollisionInQueue)
                        return;
                    if (data.DetectParams == null)
                        return;

                    m_CollisionInQueue = true;
                }

                EventQueue.Enqueue(data);

                if (m_CurrentWorkItem == null)
                {
                    m_CurrentWorkItem = Engine.QueueEventHandler(this);
                }
            }
        }

        /// <summary>
        /// Process the next event queued for this script
        /// </summary>
        /// <returns></returns>
        public object EventProcessor()
        {
            // We check here as the thread stopping this instance from running may itself hold the m_Script lock.
            if (!Running)
                return 0;

            lock (m_Script)
            {
//                m_log.DebugFormat("[XEngine]: EventProcessor() invoked for {0}.{1}", PrimName, ScriptName);

                if (Suspended)
                    return 0;

                EventParams data = null;

                lock (EventQueue)
                {
                    data = (EventParams)EventQueue.Dequeue();
                    if (data == null) // Shouldn't happen
                    {
                        if (EventQueue.Count > 0 && Running && !ShuttingDown)
                        {
                            m_CurrentWorkItem = Engine.QueueEventHandler(this);
                        }
                        else
                        {
                            m_CurrentWorkItem = null;
                        }
                        return 0;
                    }

                    if (data.EventName == "timer")
                        m_TimerQueued = false;
                    if (data.EventName == "control")
                    {
                        if (m_ControlEventsInQueue > 0)
                            m_ControlEventsInQueue--;
                    }
                    if (data.EventName == "collision")
                        m_CollisionInQueue = false;
                }
                
//                m_log.DebugFormat("[XEngine]: Processing event {0} for {1}", data.EventName, this);

                m_DetectParams = data.DetectParams;

                if (data.EventName == "state") // Hardcoded state change
                {
    //                m_log.DebugFormat("[Script] Script {0}.{1} state set to {2}",
    //                        PrimName, ScriptName, data.Params[0].ToString());
                    State = data.Params[0].ToString();
                    AsyncCommandManager.RemoveScript(Engine,
                        LocalID, ItemID);

                    SceneObjectPart part = Engine.World.GetSceneObjectPart(
                        LocalID);
                    if (part != null)
                    {
                        part.SetScriptEvents(ItemID,
                                             (int)m_Script.GetStateEventFlags(State));
                    }
                }
                else
                {
                    if (Engine.World.PipeEventsForScript(LocalID) ||
                        data.EventName == "control") // Don't freeze avies!
                    {
                        SceneObjectPart part = Engine.World.GetSceneObjectPart(
                            LocalID);
        //                m_log.DebugFormat("[Script] Delivered event {2} in state {3} to {0}.{1}",
        //                        PrimName, ScriptName, data.EventName, State);

                        try
                        {
                            m_CurrentEvent = data.EventName;
                            m_EventStart = DateTime.Now;
                            m_InEvent = true;

                            int start = Util.EnvironmentTickCount();

                            // Reset the measurement period when we reach the end of the current one.
                            if (start - MeasurementPeriodTickStart > MaxMeasurementPeriod)
                                MeasurementPeriodTickStart = start;

                            m_Script.ExecuteEvent(State, data.EventName, data.Params);

                            MeasurementPeriodExecutionTime += Util.EnvironmentTickCount() - start;

                            m_InEvent = false;
                            m_CurrentEvent = String.Empty;

                            if (m_SaveState)
                            {
                                // This will be the very first event we deliver
                                // (state_entry) in default state
                                //
                                SaveState(m_Assembly);

                                m_SaveState = false;
                            }
                        }
                        catch (Exception e)
                        {
//                            m_log.DebugFormat(
//                                "[SCRIPT] Exception in script {0} {1}: {2}{3}",
//                                ScriptName, ItemID, e.Message, e.StackTrace);

                            m_InEvent = false;
                            m_CurrentEvent = String.Empty;

                            if ((!(e is TargetInvocationException) || (!(e.InnerException is SelfDeleteException) && !(e.InnerException is ScriptDeleteException))) && !(e is ThreadAbortException))
                            {
                                try
                                {
                                    // DISPLAY ERROR INWORLD
                                    string text = FormatException(e);

                                    if (text.Length > 1000)
                                        text = text.Substring(0, 1000);
                                    Engine.World.SimChat(Utils.StringToBytes(text),
                                                           ChatTypeEnum.DebugChannel, 2147483647,
                                                           part.AbsolutePosition,
                                                           part.Name, part.UUID, false);
                                }
                                catch (Exception)
                                {
                                }
                                // catch (Exception e2) // LEGIT: User Scripting
                                // {
                                    // m_log.Error("[SCRIPT]: "+
                                      //           "Error displaying error in-world: " +
                                        //         e2.ToString());
                                 //    m_log.Error("[SCRIPT]: " +
                                   //              "Errormessage: Error compiling script:\r\n" +
                                     //            e.ToString());
                                // }
                            }
                            else if ((e is TargetInvocationException) && (e.InnerException is SelfDeleteException))
                            {
                                m_InSelfDelete = true;
                                if (part != null)
                                    Engine.World.DeleteSceneObject(part.ParentGroup, false);
                            }
                            else if ((e is TargetInvocationException) && (e.InnerException is ScriptDeleteException))
                            {
                                m_InSelfDelete = true;
                                if (part != null)
                                    part.Inventory.RemoveInventoryItem(ItemID);
                            }
                        }
                    }
                }

                // If there are more events and we are currently running and not shutting down, then ask the
                // script engine to run the next event.
                lock (EventQueue)
                {
                    if (EventQueue.Count > 0 && Running && !ShuttingDown)
                    {
                        m_CurrentWorkItem = Engine.QueueEventHandler(this);
                    }
                    else
                    {
                        m_CurrentWorkItem = null;
                    }
                }

                m_DetectParams = null;

                return 0;
            }
        }

        public int EventTime()
        {
            if (!m_InEvent)
                return 0;

            return (DateTime.Now - m_EventStart).Seconds;
        }

        public void ResetScript()
        {
            if (m_Script == null)
                return;

            bool running = Running;

            RemoveState();
            ReleaseControls();

            Stop(0);
            SceneObjectPart part = Engine.World.GetSceneObjectPart(LocalID);
            part.Inventory.GetInventoryItem(ItemID).PermsMask = 0;
            part.Inventory.GetInventoryItem(ItemID).PermsGranter = UUID.Zero;
            AsyncCommandManager.RemoveScript(Engine, LocalID, ItemID);
            EventQueue.Clear();
            m_Script.ResetVars();
            State = "default";

            part.SetScriptEvents(ItemID,
                                 (int)m_Script.GetStateEventFlags(State));
            if (running)
                Start();
            m_SaveState = true;
            PostEvent(new EventParams("state_entry",
                    new Object[0], new DetectParams[0]));
        }

        public void ApiResetScript()
        {
            // bool running = Running;

            RemoveState();
            ReleaseControls();

            m_Script.ResetVars();
            SceneObjectPart part = Engine.World.GetSceneObjectPart(LocalID);
            part.Inventory.GetInventoryItem(ItemID).PermsMask = 0;
            part.Inventory.GetInventoryItem(ItemID).PermsGranter = UUID.Zero;
            AsyncCommandManager.RemoveScript(Engine, LocalID, ItemID);

            EventQueue.Clear();
            m_Script.ResetVars();
            State = "default";

            part.SetScriptEvents(ItemID,
                                 (int)m_Script.GetStateEventFlags(State));

            if (m_CurrentEvent != "state_entry")
            {
                m_SaveState = true;
                PostEvent(new EventParams("state_entry",
                        new Object[0], new DetectParams[0]));
                throw new EventAbortException();
            }
        }

        public Dictionary<string, object> GetVars()
        {
            if (m_Script != null)
                return m_Script.GetVars();
            else
                return new Dictionary<string, object>();
        }

        public void SetVars(Dictionary<string, object> vars)
        {
            m_Script.SetVars(vars);
        }

        public DetectParams GetDetectParams(int idx)
        {
            if (m_DetectParams == null)
                return null;
            if (idx < 0 || idx >= m_DetectParams.Length)
                return null;

            return m_DetectParams[idx];
        }

        public UUID GetDetectID(int idx)
        {
            if (m_DetectParams == null)
                return UUID.Zero;
            if (idx < 0 || idx >= m_DetectParams.Length)
                return UUID.Zero;

            return m_DetectParams[idx].Key;
        }

        public void SaveState(string assembly)
        {
            // If we're currently in an event, just tell it to save upon return
            //
            if (m_InEvent)
            {
                m_SaveState = true;
                return;
            }

            PluginData = AsyncCommandManager.GetSerializationData(Engine, ItemID);

            string xml = ScriptSerializer.Serialize(this);

            // Compare hash of the state we just just created with the state last written to disk
            // If the state is different, update the disk file.
            UUID hash = UUID.Parse(Utils.MD5String(xml));

            if (hash != m_CurrentStateHash)
            {
                try
                {
                    FileStream fs = File.Create(Path.Combine(Path.GetDirectoryName(assembly), ItemID.ToString() + ".state"));
                    System.Text.UTF8Encoding enc = new System.Text.UTF8Encoding();
                    Byte[] buf = enc.GetBytes(xml);
                    fs.Write(buf, 0, buf.Length);
                    fs.Close();
                }
                catch(Exception)
                {
                    // m_log.Error("Unable to save xml\n"+e.ToString());
                }
                //if (!File.Exists(Path.Combine(Path.GetDirectoryName(assembly), ItemID.ToString() + ".state")))
                //{
                //    throw new Exception("Completed persistence save, but no file was created");
                //}
                m_CurrentStateHash = hash;
            }
        }

        public IScriptApi GetApi(string name)
        {
            if (m_Apis.ContainsKey(name))
                return m_Apis[name];
            return null;
        }
        
        public override string ToString()
        {
            return String.Format("{0} {1} on {2}", ScriptName, ItemID, PrimName);
        }

        string FormatException(Exception e)
        {
            if (e.InnerException == null) // Not a normal runtime error
                return e.ToString();

            string message = "Runtime error:\n" + e.InnerException.StackTrace;
            string[] lines = message.Split(new char[] {'\n'});

            foreach (string line in lines)
            {
                if (line.Contains("SecondLife.Script"))
                {
                    int idx = line.IndexOf(':');
                    if (idx != -1)
                    {
                        string val = line.Substring(idx+1);
                        int lineNum = 0;
                        if (int.TryParse(val, out lineNum))
                        {
                            KeyValuePair<int, int> pos =
                                    Compiler.FindErrorPosition(
                                    lineNum, 0, LineMap);

                            int scriptLine = pos.Key;
                            int col = pos.Value;
                            if (scriptLine == 0)
                                scriptLine++;
                            if (col == 0)
                                col++;
                            message = string.Format("Runtime error:\n" +
                                    "({0}): {1}", scriptLine - 1,
                                    e.InnerException.Message);

                            System.Console.WriteLine(e.ToString()+"\n");
                            return message;
                        }
                    }
                }
            }

            // m_log.ErrorFormat("Scripting exception:");
            // m_log.ErrorFormat(e.ToString());

            return e.ToString();
        }

        public string GetAssemblyName()
        {
            return m_Assembly;
        }

        public string GetXMLState()
        {
            bool run = Running;
            Stop(100);
            Running = run;

            // We should not be doing this, but since we are about to
            // dispose this, it really doesn't make a difference
            // This is meant to work around a Windows only race
            //
            m_InEvent = false;

            // Force an update of the in-memory plugin data
            //
            PluginData = AsyncCommandManager.GetSerializationData(Engine, ItemID);

            return ScriptSerializer.Serialize(this);
        }

        public UUID RegionID
        {
            get { return m_RegionID; }
        }

        public void Suspend()
        {
            Suspended = true;
        }

        public void Resume()
        {
            Suspended = false;
        }
    }
}
