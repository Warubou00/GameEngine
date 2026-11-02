/***********************************************************************************
* @file    solver.cpp
* @brief   剛体 (P1) : 質量と慣性 / 力と命令 / 接触とジョイントを解く / 位置を進める
* @details 1 回の Simulate(dt) の流れ (Box2D v3 の「柔らかい小さな刻み」(soft step) を 3 次元にしたもの)
*            1. キネマティックの目標 → 速度
*            2. 重なりを調べる (Step と同じ。剛体の組は少し離れていても接触の点を作る (SpeculativeDistance))
*            3. 接触の準備 (点ごとの有効な質量 / 前の力積の引き継ぎ) とジョイントの準備
*            4. dt を SubSteps 個に分けて、それぞれで
*                 速度を進める (重力 / 力 / 減衰) → 前の力積をかけ直す → 解く (位置のずれも柔らかく戻す) →
*                 位置を進める → 解く (速度だけ。戻しすぎた勢いを取る)
*               接触の離れ具合は、小さな刻みごとに体の動きから見積もり直す (形の判定は 1 回だけ)
*            5. 反発 (始めにぶつかった速さから跳ね返す) / 力積を組の表に残す (次の Simulate の始めにかけ直す)
*            6. 動いた剛体の形と箱を作り直す (SetTransforms と同じ道)
*          以前 (C1〜C3) は衝突判定だけで、物を動かすのは C# (Transform) だけだった
*
* @author  Keigo Onari
* @date    2026/11/03
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

/******************************************************************
* Include Headers
******************************************************************/
#include <chrono>
#include <cfloat>
#include "physicsWorld.h"

namespace Physics
{
	namespace
	{
		using Clock = std::chrono::steady_clock;
		float elapsedMs(Clock::time_point a, Clock::time_point b) { return std::chrono::duration<float, std::milli>(b - a).count(); }

		Float3 toFloat3(const float v[3]) { return Float3(v[0], v[1], v[2]); }

		// 向き 1 本の拘束を作る / 速さ / 力積をかける
		template<typename Axis>
		void makeAxis(Axis& axis, Float3 dir, Float3 rA, Float3 rB, const SolverBody& a, const SolverBody& b)
		{
			axis.Dir = dir;
			axis.RA  = cross(rA, dir);
			axis.RB  = cross(rB, dir);
			axis.IA  = a.InvInertia * axis.RA;
			axis.IB  = b.InvInertia * axis.RB;
			const float k = dot(dir, mul(a.InvMass, dir)) + dot(dir, mul(b.InvMass, dir)) + dot(axis.RA, axis.IA) + dot(axis.RB, axis.IB);
			axis.Mass = k > 0.0f ? 1.0f / k : 0.0f;
		}
		template<typename Axis>
		float axisVelocity(const Axis& axis, const SolverBody& a, const SolverBody& b)
		{
			return dot(b.V - a.V, axis.Dir) + dot(b.W, axis.RB) - dot(a.W, axis.RA);
		}
		template<typename Axis>
		void applyAxis(const Axis& axis, SolverBody& a, SolverBody& b, float impulse)
		{
			// 動かない物 / キネマティックには書かない (質量が無限で変わらない上、色の違う接触から同時に触られる)
			const Float3 p = axis.Dir * impulse;
			if (a.Dynamic)
			{
				a.V = a.V - mul(a.InvMass, p);
				a.W = a.W - axis.IA * impulse;
			}
			if (b.Dynamic)
			{
				b.V = b.V + mul(b.InvMass, p);
				b.W = b.W + axis.IB * impulse;
			}
		}
		void   store(float out[3], Float3 v) { out[0] = v.x; out[1] = v.y; out[2] = v.z; }

		// 固定した軸を 0 にした慣性の逆数 (L I L。L = 固定していない軸は 1 の対角)
		Mat3 maskInertia(const Mat3& m, Float3 mask)
		{
			const float k[3] = { mask.x, mask.y, mask.z };
			Mat3 r;
			for (int i = 0; i < 3; ++i)
				for (int j = 0; j < 3; ++j)
					r.m[i][j] = m.m[i][j] * k[i] * k[j];
			return r;
		}
	}

	// ==============================================================
	// 剛体にする / 値
	// ==============================================================

	void PhysicsWorld::setMotion(uint32_t index, MotionType motion)
	{
		Body& body = _bodies[index];
		if (body.Motion == motion) return;
		const bool wasRigid = body.Motion != MotionType::None;
		const bool isRigid  = motion != MotionType::None;
		body.Motion = motion;
		if (isRigid && !wasRigid)
		{
			body.RigidSlot = static_cast<int>(_rigidBodies.size());
			_rigidBodies.push_back(index);
			_rigidData.emplace_back();
		}
		else if (!isRigid && wasRigid)
		{
			// 最後の剛体を空いた所へ
			const uint32_t last = _rigidBodies.back();
			_rigidBodies[body.RigidSlot] = last;
			_rigidData[body.RigidSlot]   = _rigidData.back();
			_bodies[last].RigidSlot = body.RigidSlot;
			_rigidBodies.pop_back();
			_rigidData.pop_back();
			body.RigidSlot = -1;
			body.Solver    = -1;
		}
	}

	void PhysicsWorld::SetBodyDynamics(uint32_t handle, const BodyDynamics& dynamics)
	{
		Body* body = find(handle);
		if (!body) return;
		const uint32_t index = handle & 0xFFFFu;
		MotionType motion = dynamics.Motion <= 2 ? static_cast<MotionType>(dynamics.Motion) : MotionType::None;
		if (body->IsStatic) motion = MotionType::None;   // 動かない物 (Static) は剛体にしない
		setMotion(index, motion);
		if (motion == MotionType::None) return;
		rigidOf(*body).Dynamics = dynamics;

		const uint32_t locks = dynamics.Locks;
		rigidOf(*body).LinearMask  = Float3((locks & LOCK_POSITION_X) ? 0.0f : 1.0f, (locks & LOCK_POSITION_Y) ? 0.0f : 1.0f, (locks & LOCK_POSITION_Z) ? 0.0f : 1.0f);
		rigidOf(*body).AngularMask = Float3((locks & LOCK_ROTATION_X) ? 0.0f : 1.0f, (locks & LOCK_ROTATION_Y) ? 0.0f : 1.0f, (locks & LOCK_ROTATION_Z) ? 0.0f : 1.0f);
		updateMass(*body);
	}

	void PhysicsWorld::updateMass(Body& body)
	{
		if (body.Motion == MotionType::None) return;
		if (body.Motion != MotionType::Dynamic)
		{
			rigidOf(body).InvMass = 0.0f;
			rigidOf(body).InvInertiaLocal = Float3();
			return;
		}
		const float mass = rigidOf(body).Dynamics.Mass > 0.0f ? rigidOf(body).Dynamics.Mass : 1.0f;
		rigidOf(body).InvMass = 1.0f / mass;

		// 形の慣性 (一様な密度。大きさを掛けた後の形で)
		const WorldShape& w = body.World;
		Float3 inertia;
		switch (body.Shape)
		{
		case ShapeType::Sphere:
		{
			const float i = 0.4f * mass * w.Radius * w.Radius;
			inertia = Float3(i, i, i);
			break;
		}
		case ShapeType::Box:
		{
			const Float3 h = w.HalfExtents;
			const float k = mass / 3.0f;
			inertia = Float3(k * (h.y * h.y + h.z * h.z), k * (h.x * h.x + h.z * h.z), k * (h.x * h.x + h.y * h.y));
			break;
		}
		case ShapeType::Capsule:
		{
			// 円柱 (長さ L = 2 × HalfHeight) + 両端の半球。体積で質量を分ける
			const float r = w.Radius, len = 2.0f * w.HalfHeight;
			const float vc = 3.14159265f * r * r * len, vs = 4.0f / 3.0f * 3.14159265f * r * r * r;
			const float mc = mass * vc / (vc + vs), ms = mass - mc;
			const float iy = mc * r * r * 0.5f + ms * 0.4f * r * r;
			const float ix = mc * (len * len / 12.0f + r * r * 0.25f) + ms * (0.4f * r * r + len * len * 0.25f + 0.375f * len * r);
			inertia = Float3(ix, iy, ix);
			break;
		}
		default:
			inertia = Float3(mass, mass, mass);
			break;
		}
		const float scale = rigidOf(body).Dynamics.InertiaScale > 0.0f ? rigidOf(body).Dynamics.InertiaScale : 1.0f;
		auto inv = [scale](float i) { return i * scale > 1e-9f ? 1.0f / (i * scale) : 0.0f; };
		rigidOf(body).InvInertiaLocal = Float3(inv(inertia.x), inv(inertia.y), inv(inertia.z));
	}

	void PhysicsWorld::ApplyCommands(const BodyCommand* commands, uint32_t count)
	{
		for (uint32_t i = 0; i < count; ++i)
		{
			const BodyCommand& c = commands[i];
			Body* body = find(c.Handle);
			if (!body || body->Motion == MotionType::None) continue;
			const bool dynamic = body->Motion == MotionType::Dynamic;
			if ((c.Flags & COMMAND_FORCE) && dynamic)
			{
				rigidOf(*body).Force  = rigidOf(*body).Force  + toFloat3(c.Force);
				rigidOf(*body).Torque = rigidOf(*body).Torque + toFloat3(c.Torque);
			}
			if ((c.Flags & COMMAND_IMPULSE) && dynamic)
			{
				rigidOf(*body).Velocity = rigidOf(*body).Velocity + mul(rigidOf(*body).LinearMask, toFloat3(c.Impulse) * rigidOf(*body).InvMass);
				const Mat3 invI = maskInertia(rotateDiagonal(body->World.Rotation, rigidOf(*body).InvInertiaLocal), rigidOf(*body).AngularMask);
				rigidOf(*body).AngularVelocity = rigidOf(*body).AngularVelocity + invI * toFloat3(c.AngularImpulse);
			}
			if (c.Flags & COMMAND_SET_VELOCITY)
			{
				rigidOf(*body).Velocity = dynamic ? mul(rigidOf(*body).LinearMask, toFloat3(c.Velocity)) : toFloat3(c.Velocity);
				rigidOf(*body).ClearVelocityAfterStep = false;
			}
			if (c.Flags & COMMAND_SET_ANGULAR)
			{
				rigidOf(*body).AngularVelocity = dynamic ? mul(rigidOf(*body).AngularMask, toFloat3(c.AngularVelocity)) : toFloat3(c.AngularVelocity);
			}
			if ((c.Flags & COMMAND_KINEMATIC_TARGET) && !dynamic)
			{
				rigidOf(*body).HasTarget      = true;
				rigidOf(*body).TargetPosition = toFloat3(c.TargetPosition);
				rigidOf(*body).TargetRotation = normalize(Quat(c.TargetRotation[0], c.TargetRotation[1], c.TargetRotation[2], c.TargetRotation[3]));
			}
		}
	}

	uint32_t PhysicsWorld::GetBodyStates(BodyState* out, uint32_t capacity) const
	{
		const uint32_t count = static_cast<uint32_t>(_rigidBodies.size());
		if (!out) return count;
		const uint32_t n = (std::min)(count, capacity);
		for (uint32_t i = 0; i < n; ++i)
		{
			const uint32_t index = _rigidBodies[i];
			const Body& body = _bodies[index];
			BodyState& s = out[i];
			s = BodyState();
			s.Handle = handleOf(index);
			store(s.Position, body.Position);
			s.Rotation[0] = body.Rotation.x; s.Rotation[1] = body.Rotation.y; s.Rotation[2] = body.Rotation.z; s.Rotation[3] = body.Rotation.w;
			store(s.Velocity, rigidOf(body).Velocity);
			store(s.AngularVelocity, rigidOf(body).AngularVelocity);
			store(s.CenterOfMass, body.World.Center);
		}
		return count;
	}

	// ==============================================================
	// Simulate
	// ==============================================================

	uint32_t PhysicsWorld::Simulate(float dt)
	{
		if (dt <= 0.0f) return Step();
		const float invDt = 1.0f / dt;

		// 1. キネマティックの目標 → 速度 (重心が目標の位置の重心へ、dt で着く速さ)
		for (const uint32_t index : _rigidBodies)
		{
			Body& body = _bodies[index];
			if (!rigidOf(body).HasTarget) continue;
			rigidOf(body).HasTarget = false;
			const Float3 targetCenter = rigidOf(body).TargetPosition + rotate(rigidOf(body).TargetRotation, mul(body.LocalCenter, body.Scale));
			rigidOf(body).Velocity        = (targetCenter - body.World.Center) * invDt;
			rigidOf(body).AngularVelocity = angularVelocityBetween(body.Rotation, rigidOf(body).TargetRotation, invDt);
			rigidOf(body).ClearVelocityAfterStep = true;
		}

		// 2. 重なり (イベントも作る)
		const uint32_t eventCount = Step();

		// 3〜5. 解く
		const Clock::time_point start = Clock::now();
		solveRigid(dt);

		// 6. 動いた剛体を木へ (SetTransforms と同じ。並列で形を作り直し、木の葉を広げる)
		_poseBuffer.clear();
		for (const uint32_t index : _rigidBodies)
		{
			Body& body = _bodies[index];
			if (body.Solver < 0) continue;
			const SolverBody& s = _solverBodies[body.Solver];
			rigidOf(body).Velocity        = s.V;
			rigidOf(body).AngularVelocity = s.W;
			rigidOf(body).Force = rigidOf(body).Torque = Float3();
			if (rigidOf(body).ClearVelocityAfterStep)
			{
				rigidOf(body).ClearVelocityAfterStep = false;
				rigidOf(body).Velocity = rigidOf(body).AngularVelocity = Float3();
			}
			if (lengthSq(s.DP) == 0.0f && s.Q.x == s.Q0.x && s.Q.y == s.Q0.y && s.Q.z == s.Q0.z && s.Q.w == s.Q0.w) continue;   // 動いていない
			const Float3 center = s.Center0 + s.DP;
			const Float3 position = center - rotate(s.Q, mul(body.LocalCenter, body.Scale));
			BodyTransform t {};
			t.Handle = handleOf(index);
			store(t.Position, position);
			t.Rotation[0] = s.Q.x; t.Rotation[1] = s.Q.y; t.Rotation[2] = s.Q.z; t.Rotation[3] = s.Q.w;
			store(t.Scale, body.Scale);
			_poseBuffer.push_back(t);
		}
		if (!_poseBuffer.empty()) SetTransforms(_poseBuffer.data(), static_cast<uint32_t>(_poseBuffer.size()));

		_stats.SolverMs           = elapsedMs(start, Clock::now());
		_stats.RigidBodies        = static_cast<uint32_t>(_rigidBodies.size());
		_stats.ContactConstraints = static_cast<uint32_t>(_contacts.size());
		_stats.Joints             = _activeJoints;
		return eventCount;
	}

	void PhysicsWorld::solveRigid(float dt)
	{
		const uint32_t subSteps = (std::max)(1u, _solver.SubSteps);
		SolverStep step;
		step.H    = dt / static_cast<float>(subSteps);
		step.InvH = 1.0f / step.H;
		// ジョイントの硬さ : 小さな刻みの 1/4 の振動数まで (それより硬いと解けずに震える)
		step.JointSoftness = makeSoft((std::min)(_solver.JointHertz, 0.25f * step.InvH), _solver.JointDampingRatio, step.H);
		const Float3 gravity = toFloat3(_solver.Gravity);

		// 体の値
		_solverBodies.resize(_rigidBodies.size());
		for (size_t k = 0; k < _rigidBodies.size(); ++k)
		{
			Body& body = _bodies[_rigidBodies[k]];
			body.Solver = static_cast<int>(k);
			SolverBody& s = _solverBodies[k];
			s.V       = rigidOf(body).Velocity;
			s.W       = rigidOf(body).AngularVelocity;
			s.DP      = Float3();
			s.Q = s.Q0 = body.Rotation;
			s.Center0 = body.World.Center;
			s.Dynamic = body.Motion == MotionType::Dynamic;
			if (body.Motion == MotionType::Dynamic)
			{
				s.InvMass    = rigidOf(body).LinearMask * rigidOf(body).InvMass;
				s.InvInertia = maskInertia(rotateDiagonal(body.World.Rotation, rigidOf(body).InvInertiaLocal), rigidOf(body).AngularMask);
			}
			else
			{
				s.InvMass    = Float3();
				s.InvInertia = Mat3();
			}
		}
		_staticBody = SolverBody();

		// ジョイントの準備 (壊れた / 体の無いジョイントは飛ばす)
		std::vector<Joint*> active;
		active.reserve(_joints.size());
		for (const std::unique_ptr<Joint>& joint : _joints)
		{
			if (joint && !joint->Broken && prepareJoint(*joint))
			{
				joint->Prepare(step);
				active.push_back(joint.get());
			}
		}
		_activeJoints = static_cast<uint32_t>(active.size());

		prepareContacts(step);
		colorContacts();

		const float maxSpeed = _solver.MaxLinearSpeed > 0.0f ? _solver.MaxLinearSpeed : 400.0f;
		const float maxRotation = 0.25f * 3.14159265f * step.InvH;   // 小さな刻みで 45 度まで
		const uint32_t iterations = (std::max)(1u, _solver.Iterations);
		for (uint32_t sub = 0; sub < subSteps; ++sub)
		{
			// 速度を進める (体ごとに別なので並列)
			_jobs.ParallelFor(static_cast<uint32_t>(_rigidBodies.size()), 256, [&](uint32_t begin, uint32_t end)
			{
			for (uint32_t k = begin; k < end; ++k)
			{
				const Body& body = _bodies[_rigidBodies[k]];
				if (body.Motion != MotionType::Dynamic) continue;
				SolverBody& s = _solverBodies[k];
				const float linearDamping  = 1.0f / (1.0f + step.H * rigidOf(body).Dynamics.LinearDamping);
				const float angularDamping = 1.0f / (1.0f + step.H * rigidOf(body).Dynamics.AngularDamping);
				Float3 v = (s.V + (gravity * rigidOf(body).Dynamics.GravityScale + rigidOf(body).Force * rigidOf(body).InvMass) * step.H) * linearDamping;
				Float3 w = (s.W + (s.InvInertia * rigidOf(body).Torque) * step.H) * angularDamping;
				v = mul(v, rigidOf(body).LinearMask);
				w = mul(w, rigidOf(body).AngularMask);
				const float speed = length(v);
				if (speed > maxSpeed) v = v * (maxSpeed / speed);
				const float spin = length(w);
				if (spin > maxRotation) w = w * (maxRotation / spin);
				s.V = v;
				s.W = w;
			}
			});

			// 前の力積をかけ直す (ジョイントは数が少ないので順番に。接触は色ごとに並列)
			for (Joint* joint : active) joint->WarmStart();
			forEachColor([&](uint32_t begin, uint32_t end) { warmStartContacts(begin, end); });

			// 解く (位置のずれも戻す)
			for (uint32_t it = 0; it < iterations; ++it)
			{
				for (Joint* joint : active) joint->Solve(step, true);
				forEachColor([&](uint32_t begin, uint32_t end) { solveContacts(step, true, begin, end); });
			}

			// 位置を進める
			_jobs.ParallelFor(static_cast<uint32_t>(_solverBodies.size()), 256, [&](uint32_t begin, uint32_t end)
			{
				for (uint32_t k = begin; k < end; ++k)
				{
					SolverBody& s = _solverBodies[k];
					s.DP = s.DP + s.V * step.H;
					s.Q  = integrate(s.Q, s.W, step.H);
				}
			});

			// 解く (速度だけ。戻しすぎた勢いを取る)
			for (uint32_t it = 0; it < _solver.RelaxIterations; ++it)
			{
				for (Joint* joint : active) joint->Solve(step, false);
				forEachColor([&](uint32_t begin, uint32_t end) { solveContacts(step, false, begin, end); });
			}
		}

		applyRestitution();
		storeImpulses();

		// ジョイントの力 / 壊れたか
		for (Joint* joint : active)
		{
			joint->LastForce = joint->Force(step.InvH);
			if (joint->Desc.BreakForce > 0.0f && joint->LastForce > joint->Desc.BreakForce)
			{
				joint->Broken = true;
				releaseJointPair(*joint);
			}
		}
	}

	// ==============================================================
	// 接触
	// ==============================================================

	void PhysicsWorld::prepareContacts(const SolverStep& step)
	{
		_contacts.clear();
		const float contactHertz = (std::min)(_solver.ContactHertz, 0.25f * step.InvH);
		const Softness soft       = makeSoft(contactHertz, _solver.ContactDampingRatio, step.H);
		const Softness staticSoft = makeSoft(2.0f * contactHertz, _solver.ContactDampingRatio, step.H);   // 動かない相手は硬めに (Box2D と同じ)

		for (uint32_t pairIndex = 0; pairIndex < _pairs.size(); ++pairIndex)
		{
			const Pair& pair = _pairs[pairIndex];
			if (!pair.Physical || pair.Contact.Count == 0) continue;
			const Body& a = _bodies[pair.A];
			const Body& b = _bodies[pair.B];
			if (!isPhysicalPair(a, b)) continue;
			if (!_noCollidePairs.empty() && _noCollidePairs.count(pair.Key)) continue;   // ジョイントで繋いだ組

			ContactConstraint c;
			c.A = solverOf(a);
			c.B = solverOf(b);
			c.Pair        = pairIndex;
			c.Count       = pair.Contact.Count;
			c.Friction    = std::sqrt((std::max)(0.0f, a.Friction) * (std::max)(0.0f, b.Friction));
			c.Restitution = (std::max)(a.Restitution, b.Restitution);
			const bool staticPartner = a.Motion != MotionType::Dynamic || b.Motion != MotionType::Dynamic;
			c.Soft = staticPartner ? staticSoft : soft;

			const SolverBody& sa = *c.A;
			const SolverBody& sb = *c.B;
			const Float3 centerA = a.World.Center, centerB = b.World.Center;
			for (int i = 0; i < c.Count; ++i)
			{
				const ContactPoint& p = pair.Contact.Points[i];
				ContactPointConstraint& cp = c.Points[i];
				Float3 t1, t2;
				tangentBasis(p.Normal, t1, t2);
				cp.RA = p.Position - centerA;
				cp.RB = p.Position - centerB;
				cp.AdjustedSeparation = -p.Depth - dot(cp.RB - cp.RA, p.Normal);
				makeAxis(cp.N,  p.Normal, cp.RA, cp.RB, sa, sb);
				makeAxis(cp.T1, t1,       cp.RA, cp.RB, sa, sb);
				makeAxis(cp.T2, t2,       cp.RA, cp.RB, sa, sb);

				// 前の Simulate の力積を引き継ぐ (摩擦はワールドの向きで持っているので、今の 2 本の軸へ移す)
				const PairImpulse& warm = _pairImpulses[pairIndex];
				cp.N.Impulse  = warm.Normal[i];
				cp.T1.Impulse = dot(warm.Friction[i], t1);
				cp.T2.Impulse = dot(warm.Friction[i], t2);
				cp.MaxNormalImpulse = 0.0f;
				cp.RelativeVelocity = axisVelocity(cp.N, sa, sb);
			}
			_contacts.push_back(c);
		}
	}

	void PhysicsWorld::colorContacts()
	{
		// ==============================================================
		// 色分け (Box2D v3 と同じ) : 接触ごとに、両方の動く体がまだ使われていない一番小さい色に入れる
		//   同じ色の接触は同じ動く体を触らないので、並列に解いても結果は順番に解いた時と同じ (毎回同じ結果)
		//   動かない物 / キネマティックは書き換えないので、何色からでも触ってよい
		//   MAX_COLORS に入らない分は「あふれ」として最後に順番に解く
		// ==============================================================
		const size_t count = _contacts.size();
		const size_t words = (_solverBodies.size() + 63) / 64;
		_colorBits.assign(MAX_COLORS * words, 0ull);
		_contactColor.resize(count);
		uint32_t colorCount[MAX_COLORS + 1] = {};
		const SolverBody* base = _solverBodies.data();
		for (size_t i = 0; i < count; ++i)
		{
			const ContactConstraint& c = _contacts[i];
			const int ia = c.A->Dynamic ? static_cast<int>(c.A - base) : -1;
			const int ib = c.B->Dynamic ? static_cast<int>(c.B - base) : -1;
			int color = MAX_COLORS;
			for (int k = 0; k < MAX_COLORS; ++k)
			{
				uint64_t* bits = _colorBits.data() + k * words;
				if (ia >= 0 && (bits[ia >> 6] >> (ia & 63) & 1ull)) continue;
				if (ib >= 0 && (bits[ib >> 6] >> (ib & 63) & 1ull)) continue;
				if (ia >= 0) bits[ia >> 6] |= 1ull << (ia & 63);
				if (ib >= 0) bits[ib >> 6] |= 1ull << (ib & 63);
				color = k;
				break;
			}
			_contactColor[i] = static_cast<uint8_t>(color);
			colorCount[color]++;
		}
		// 色の順に並べ直す (同じ色が続いたメモリになる)
		_colorStart[0] = 0;
		for (int k = 0; k <= MAX_COLORS; ++k) _colorStart[k + 1] = _colorStart[k] + colorCount[k];
		uint32_t cursor[MAX_COLORS + 1];
		for (int k = 0; k <= MAX_COLORS; ++k) cursor[k] = _colorStart[k];
		_coloredContacts.resize(count);
		for (size_t i = 0; i < count; ++i) _coloredContacts[cursor[_contactColor[i]]++] = _contacts[i];
		_contacts.swap(_coloredContacts);
	}

	template<typename Fn>
	void PhysicsWorld::forEachColor(Fn&& fn)
	{
		for (int k = 0; k < MAX_COLORS; ++k)
		{
			const uint32_t begin = _colorStart[k], count = _colorStart[k + 1] - begin;
			if (count == 0) continue;
			_jobs.ParallelFor(count, 64, [&](uint32_t b, uint32_t e) { fn(begin + b, begin + e); });
		}
		// あふれた分 (順番に)
		if (_colorStart[MAX_COLORS + 1] > _colorStart[MAX_COLORS]) fn(_colorStart[MAX_COLORS], _colorStart[MAX_COLORS + 1]);
	}

	void PhysicsWorld::warmStartContacts(uint32_t begin, uint32_t end)
	{
		for (uint32_t index = begin; index < end; ++index)
		{
			ContactConstraint& c = _contacts[index];
			SolverBody& a = *c.A;
			SolverBody& b = *c.B;
			for (int i = 0; i < c.Count; ++i)
			{
				const ContactPointConstraint& cp = c.Points[i];
				const Float3 p = cp.N.Dir * cp.N.Impulse + cp.T1.Dir * cp.T1.Impulse + cp.T2.Dir * cp.T2.Impulse;
				if (a.Dynamic)
				{
					a.V = a.V - mul(a.InvMass, p);
					a.W = a.W - (cp.N.IA * cp.N.Impulse + cp.T1.IA * cp.T1.Impulse + cp.T2.IA * cp.T2.Impulse);
				}
				if (b.Dynamic)
				{
					b.V = b.V + mul(b.InvMass, p);
					b.W = b.W + (cp.N.IB * cp.N.Impulse + cp.T1.IB * cp.T1.Impulse + cp.T2.IB * cp.T2.Impulse);
				}
			}
		}
	}

	void PhysicsWorld::solveContacts(const SolverStep& step, bool useBias, uint32_t begin, uint32_t end)
	{
		const float maxPush = _solver.ContactPushMaxVelocity;
		for (uint32_t index = begin; index < end; ++index)
		{
			ContactConstraint& c = _contacts[index];
			SolverBody& a = *c.A;
			SolverBody& b = *c.B;
			// 始めからの回転 (点を今の向きへ回して、離れ具合を見積もり直す)
			const Quat dqA = a.Q * conjugate(a.Q0);
			const Quat dqB = b.Q * conjugate(b.Q0);
			const Float3 dp = b.DP - a.DP;

			for (int i = 0; i < c.Count; ++i)
			{
				ContactPointConstraint& cp = c.Points[i];
				const Float3 d = dp + (rotate(dqB, cp.RB) - rotate(dqA, cp.RA));
				const float s = dot(d, cp.N.Dir) + cp.AdjustedSeparation;

				float bias = 0.0f, massScale = 1.0f, impulseScale = 0.0f;
				if (s > 0.0f)
				{
					bias = s * step.InvH;   // まだ離れている : その距離までは近づいてよい (少し先の接触)
				}
				else if (useBias)
				{
					bias         = (std::max)(c.Soft.BiasRate * s, -maxPush);
					massScale    = c.Soft.MassScale;
					impulseScale = c.Soft.ImpulseScale;
				}

				const float vn = axisVelocity(cp.N, a, b);
				float impulse = -cp.N.Mass * massScale * (vn + bias) - impulseScale * cp.N.Impulse;
				const float next = (std::max)(cp.N.Impulse + impulse, 0.0f);
				impulse = next - cp.N.Impulse;
				cp.N.Impulse = next;
				cp.MaxNormalImpulse = (std::max)(cp.MaxNormalImpulse, impulse);
				applyAxis(cp.N, a, b, impulse);
			}

			// 摩擦 (2 本の軸をまとめて、法線の力積 × 摩擦 の円の中に収める)
			for (int i = 0; i < c.Count; ++i)
			{
				ContactPointConstraint& cp = c.Points[i];
				float t1 = cp.T1.Impulse - cp.T1.Mass * axisVelocity(cp.T1, a, b);
				float t2 = cp.T2.Impulse - cp.T2.Mass * axisVelocity(cp.T2, a, b);
				const float maxFriction = c.Friction * cp.N.Impulse;
				const float magnitude = std::sqrt(t1 * t1 + t2 * t2);
				if (magnitude > maxFriction && magnitude > 0.0f)
				{
					const float k = maxFriction / magnitude;
					t1 *= k;
					t2 *= k;
				}
				applyAxis(cp.T1, a, b, t1 - cp.T1.Impulse);
				applyAxis(cp.T2, a, b, t2 - cp.T2.Impulse);
				cp.T1.Impulse = t1;
				cp.T2.Impulse = t2;
			}
		}
	}

	void PhysicsWorld::applyRestitution()
	{
		const float threshold = _solver.RestitutionThreshold;
		for (ContactConstraint& c : _contacts)
		{
			if (c.Restitution <= 0.0f) continue;
			SolverBody& a = *c.A;
			SolverBody& b = *c.B;
			for (int i = 0; i < c.Count; ++i)
			{
				ContactPointConstraint& cp = c.Points[i];
				// ゆっくり当たった点 / 力のかからなかった点 (少し先の接触) は跳ねない
				if (cp.RelativeVelocity > -threshold || cp.MaxNormalImpulse == 0.0f) continue;
				const float vn = axisVelocity(cp.N, a, b);
				float impulse = -cp.N.Mass * (vn + c.Restitution * cp.RelativeVelocity);
				const float next = (std::max)(cp.N.Impulse + impulse, 0.0f);
				impulse = next - cp.N.Impulse;
				cp.N.Impulse = next;
				applyAxis(cp.N, a, b, impulse);
			}
		}
	}

	void PhysicsWorld::storeImpulses()
	{
		for (const ContactConstraint& c : _contacts)
		{
			PairImpulse& warm = _pairImpulses[c.Pair];
			for (int i = 0; i < c.Count; ++i)
			{
				const ContactPointConstraint& cp = c.Points[i];
				warm.Normal[i]   = cp.N.Impulse;
				warm.Friction[i] = cp.T1.Dir * cp.T1.Impulse + cp.T2.Dir * cp.T2.Impulse;
			}
		}
	}

	// ==============================================================
	// ジョイント
	// ==============================================================

	uint32_t PhysicsWorld::CreateJoint(const JointDesc& source)
	{
		Body* a = find(source.BodyA);
		if (!a) return INVALID_HANDLE;
		Body* b = source.BodyB == INVALID_HANDLE ? nullptr : find(source.BodyB);
		if (source.BodyB != INVALID_HANDLE && (!b || b == a)) return INVALID_HANDLE;

		JointDesc desc = source;
		// 作った時の 2 点 (ワールド)
		const Float3 worldA = a->Position + rotate(a->Rotation, mul(toFloat3(desc.AnchorA), a->Scale));
		const Float3 worldB = b ? b->Position + rotate(b->Rotation, mul(toFloat3(desc.AnchorB), b->Scale)) : toFloat3(desc.AnchorB);
		if (static_cast<JointType>(desc.Type) == JointType::Distance)
		{
			if (desc.Length <= 0.0f) desc.Length = length(worldB - worldA);
			if (desc.MinLength <= 0.0f) desc.MinLength = desc.Length;
			if (desc.MaxLength <= 0.0f) desc.MaxLength = desc.Length;
			desc.MinLength = (std::min)(desc.MinLength, desc.Length);
			desc.MaxLength = (std::max)(desc.MaxLength, desc.Length);
		}
		const Quat relative = (b ? b->Rotation : Quat()) * conjugate(a->Rotation);
		std::unique_ptr<Joint> joint = Physics::CreateJoint(desc, relative);
		if (!joint) return INVALID_HANDLE;
		joint->A.Handle = desc.BodyA;
		joint->B.Handle = desc.BodyB;

		uint32_t index;
		if (!_freeJoints.empty())
		{
			index = _freeJoints.back();
			_freeJoints.pop_back();
		}
		else
		{
			if (_joints.size() >= 0xFFFF) return INVALID_HANDLE;
			index = static_cast<uint32_t>(_joints.size());
			_joints.emplace_back();
			_jointVersions.push_back(0);
		}
		_jointVersions[index]++;
		if (_jointVersions[index] == 0) _jointVersions[index] = 1;
		_joints[index] = std::move(joint);

		if (b && !(desc.Flags & JOINT_COLLIDE_CONNECTED)) _noCollidePairs[pairKey(source.BodyA & 0xFFFFu, source.BodyB & 0xFFFFu)]++;
		return (uint32_t(_jointVersions[index]) << 16) | index;
	}

	Joint* PhysicsWorld::findJoint(uint32_t handle) const
	{
		const uint32_t index = handle & 0xFFFFu;
		if (handle == INVALID_HANDLE || index >= _joints.size() || !_joints[index]) return nullptr;
		if (_jointVersions[index] != (handle >> 16)) return nullptr;
		return _joints[index].get();
	}

	void PhysicsWorld::releaseJointPair(const Joint& joint)
	{
		if (joint.B.Handle == INVALID_HANDLE || (joint.Desc.Flags & JOINT_COLLIDE_CONNECTED)) return;
		const uint64_t key = pairKey(joint.A.Handle & 0xFFFFu, joint.B.Handle & 0xFFFFu);
		auto it = _noCollidePairs.find(key);
		if (it != _noCollidePairs.end() && --it->second <= 0) _noCollidePairs.erase(it);
	}

	void PhysicsWorld::DestroyJoint(uint32_t handle)
	{
		Joint* joint = findJoint(handle);
		if (!joint) return;
		if (!joint->Broken) releaseJointPair(*joint);
		const uint32_t index = handle & 0xFFFFu;
		_joints[index].reset();
		_freeJoints.push_back(index);
	}

	bool PhysicsWorld::GetJointInfo(uint32_t handle, JointInfo& info) const
	{
		const Joint* joint = findJoint(handle);
		if (!joint) return false;
		info = JointInfo();
		info.Broken = joint->Broken ? 1u : 0u;
		info.Force  = joint->LastForce;
		info.Length = joint->CurrentLength();
		return true;
	}

	bool PhysicsWorld::prepareJoint(Joint& joint)
	{
		// 体の参照と、重心からの点 (体のローカルの向き)。体が消えていたら壊れた扱い
		auto resolve = [&](JointBodyRef& ref, const float anchor[3]) -> bool
		{
			if (ref.Handle == INVALID_HANDLE)
			{
				ref.Fixed = SolverBody();
				ref.Fixed.Center0 = toFloat3(anchor);
				ref.Body = &ref.Fixed;
				ref.LocalAnchor = Float3();
				return true;
			}
			Body* body = find(ref.Handle);
			if (!body) return false;
			ref.LocalAnchor = mul(toFloat3(anchor) - body->LocalCenter, body->Scale);
			if (body->Solver >= 0 && body->Motion != MotionType::None)
			{
				ref.Body = &_solverBodies[body->Solver];
			}
			else
			{
				ref.Fixed = SolverBody();
				ref.Fixed.Center0 = body->World.Center;
				ref.Fixed.Q = ref.Fixed.Q0 = body->Rotation;
				ref.Body = &ref.Fixed;
			}
			return true;
		};
		if (!resolve(joint.A, joint.Desc.AnchorA) || !resolve(joint.B, joint.Desc.AnchorB))
		{
			joint.Broken = true;
			releaseJointPair(joint);
			return false;
		}
		return true;
	}
}

/************************************
* End of file
************************************/
