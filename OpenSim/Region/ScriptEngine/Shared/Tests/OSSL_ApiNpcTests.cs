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
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using NUnit.Framework;
using OpenMetaverse;
using OpenMetaverse.Assets;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.CoreModules.Avatar.AvatarFactory;
using OpenSim.Region.OptionalModules.World.NPC;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Services.Interfaces;
using OpenSim.Tests.Common;
using OpenSim.Tests.Common.Mock;

namespace OpenSim.Region.ScriptEngine.Shared.Tests
{
    /// <summary>
    /// Tests for OSSL NPC API
    /// </summary>
    [TestFixture]
    public class OSSL_NpcApiAppearanceTest
    {
        protected Scene m_scene;
        protected XEngine.XEngine m_engine;

        [SetUp]
        public void SetUp()
        {
            IConfigSource initConfigSource = new IniConfigSource();
            IConfig config = initConfigSource.AddConfig("XEngine");
            config.Set("Enabled", "true");
            config.Set("AllowOSFunctions", "true");
            config.Set("OSFunctionThreatLevel", "Severe");
            config = initConfigSource.AddConfig("NPC");
            config.Set("Enabled", "true");

            m_scene = SceneHelpers.SetupScene();
            SceneHelpers.SetupSceneModules(m_scene, initConfigSource, new AvatarFactoryModule(), new NPCModule());

            m_engine = new XEngine.XEngine();
            m_engine.Initialise(initConfigSource);
            m_engine.AddRegion(m_scene);
        }

        /// <summary>
        /// Test removal of an owned NPC.
        /// </summary>
        [Test]
        public void TestOsNpcRemoveOwned()
        {
            TestHelpers.InMethod();
//            log4net.Config.XmlConfigurator.Configure();

            // Store an avatar with a different height from default in a notecard.
            UUID userId = TestHelpers.ParseTail(0x1);
            UUID otherUserId = TestHelpers.ParseTail(0x2);
            float newHeight = 1.9f;

            SceneHelpers.AddScenePresence(m_scene, otherUserId);

            ScenePresence sp = SceneHelpers.AddScenePresence(m_scene, userId);
            sp.Appearance.AvatarHeight = newHeight;

            SceneObjectGroup so = SceneHelpers.CreateSceneObject(1, userId);
            SceneObjectPart part = so.RootPart;
            m_scene.AddSceneObject(so);

            SceneObjectGroup otherSo = SceneHelpers.CreateSceneObject(1, otherUserId);
            SceneObjectPart otherPart = otherSo.RootPart;
            m_scene.AddSceneObject(otherSo);

            OSSL_Api osslApi = new OSSL_Api();
            osslApi.Initialize(m_engine, part, part.LocalId, part.UUID);

            OSSL_Api otherOsslApi = new OSSL_Api();
            otherOsslApi.Initialize(m_engine, otherPart, otherPart.LocalId, otherPart.UUID);

            string notecardName = "appearanceNc";
            osslApi.osOwnerSaveAppearance(notecardName);

            string npcRaw
                = osslApi.osNpcCreate(
                    "Jane", "Doe", new LSL_Types.Vector3(128, 128, 128), notecardName, ScriptBaseClass.OS_NPC_CREATOR_OWNED);
            
            otherOsslApi.osNpcRemove(npcRaw);

            // Should still be around
            UUID npcId = new UUID(npcRaw);
            ScenePresence npc = m_scene.GetScenePresence(npcId);
            Assert.That(npc, Is.Not.Null);

            osslApi.osNpcRemove(npcRaw);

            npc = m_scene.GetScenePresence(npcId);

            // Now the owner deleted it and it's gone
            Assert.That(npc, Is.Null);
        }

        /// <summary>
        /// Test removal of an unowned NPC.
        /// </summary>
        [Test]
        public void TestOsNpcRemoveUnowned()
        {
            TestHelpers.InMethod();
//            log4net.Config.XmlConfigurator.Configure();

            // Store an avatar with a different height from default in a notecard.
            UUID userId = TestHelpers.ParseTail(0x1);
            float newHeight = 1.9f;

            ScenePresence sp = SceneHelpers.AddScenePresence(m_scene, userId);
            sp.Appearance.AvatarHeight = newHeight;
            SceneObjectGroup so = SceneHelpers.CreateSceneObject(1, userId);
            SceneObjectPart part = so.RootPart;
            m_scene.AddSceneObject(so);

            OSSL_Api osslApi = new OSSL_Api();
            osslApi.Initialize(m_engine, part, part.LocalId, part.UUID);

            string notecardName = "appearanceNc";
            osslApi.osOwnerSaveAppearance(notecardName);

            string npcRaw
                = osslApi.osNpcCreate(
                    "Jane", "Doe", new LSL_Types.Vector3(128, 128, 128), notecardName, ScriptBaseClass.OS_NPC_NOT_OWNED);
            
            osslApi.osNpcRemove(npcRaw);

            UUID npcId = new UUID(npcRaw);
            ScenePresence npc = m_scene.GetScenePresence(npcId);
            Assert.That(npc, Is.Null);
        }
    }
}