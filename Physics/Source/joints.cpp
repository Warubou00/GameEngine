/***********************************************************************************
* @file    joints.cpp
* @brief   ジョイント。説明は joints.h
* @details 解き方は Box2D v3 のジョイントを 3 次元にしたもの (柔らかい拘束 + 前の力の引き継ぎ)
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
#include "joints.h"

namespace Physics
{
	float Joint::CurrentLength() const
	{
		if (!A.Body || !B.Body) return 0.0f;
		return length((B.Body->Center() + anchorB()) - (A.Body->Center() + anchorA()));
	}

	namespace
	{
		// 2 点の間の、向き axis に沿った有効な質量 (1 / (動きにくさ))
		float axialMass(const SolverBody& a, const SolverBody& b, Float3 rA, Float3 rB, Float3 axis)
		{
			const Float3 ra = cross(rA, axis), rb = cross(rB, axis);
			const float k = dot(axis, mul(a.InvMass, axis)) + dot(axis, mul(b.InvMass, axis)) +
			                dot(ra, a.InvInertia * ra) + dot(rb, b.InvInertia * rb);
			return k > 0.0f ? 1.0f / k : 0.0f;
		}

		// 2 点の相対速度 (B - A)
		Float3 relativeVelocity(const SolverBody& a, const SolverBody& b, Float3 rA, Float3 rB)
		{
			return (b.V + cross(b.W, rB)) - (a.V + cross(a.W, rA));
		}

		void applyPair(SolverBody& a, SolverBody& b, Float3 rA, Float3 rB, Float3 p)
		{
			a.ApplyImpulse(rA, -p);
			b.ApplyImpulse(rB, p);
		}

		// 点を合わせる 3x3 の有効な質量の逆 (K = mA + mB - [rA]x IA [rA]x - [rB]x IB [rB]x)
		Mat3 pointMass(const SolverBody& a, const SolverBody& b, Float3 rA, Float3 rB)
		{
			const Mat3 sa = skew(rA), sb = skew(rB);
			Mat3 k = Mat3::Diagonal(a.InvMass + b.InvMass);
			const Mat3 ka = sa * a.InvInertia * sa;
			const Mat3 kb = sb * b.InvInertia * sb;
			for (int i = 0; i < 3; ++i)
				for (int j = 0; j < 3; ++j)
					k.m[i][j] -= ka.m[i][j] + kb.m[i][j];
			return k;
		}

		Softness jointSoftness(const JointDesc& desc, const SolverStep& step)
		{
			return desc.Hertz > 0.0f ? makeSoft(desc.Hertz, desc.DampingRatio, step.H) : step.JointSoftness;
		}

		// ==============================================================
		// Distance : 2 点の距離を MinLength〜MaxLength に保つ。Hertz > 0 なら Length へ戻るばね
		//   Min == Max (既定) で Hertz = 0 なら硬い棒
		// ==============================================================
		class DistanceJoint final : public Joint
		{
		public:
			void Prepare(const SolverStep& step) override
			{
				_spring = Desc.Hertz > 0.0f ? makeSoft(Desc.Hertz, Desc.DampingRatio, step.H) : Softness();
			}

			void WarmStart() override
			{
				const Float3 rA = anchorA(), rB = anchorB();
				const Float3 axis = currentAxis(rA, rB, nullptr);
				applyPair(*A.Body, *B.Body, rA, rB, axis * (_impulse + _lower - _upper));
			}

			void Solve(const SolverStep& step, bool useBias) override
			{
				SolverBody& a = *A.Body;
				SolverBody& b = *B.Body;
				const Float3 rA = anchorA(), rB = anchorB();
				float len = 0.0f;
				const Float3 axis = currentAxis(rA, rB, &len);
				const float mass = axialMass(a, b, rA, rB, axis);
				const float minLength = Desc.MinLength, maxLength = Desc.MaxLength;

				if (minLength < maxLength)
				{
					// ばね (位置のずれも速度も、ばねの硬さで戻す。押し戻しの勢いを取る時もかける)
					if (Desc.Hertz > 0.0f)
					{
						const float c = len - Desc.Length;
						const float cdot = dot(axis, relativeVelocity(a, b, rA, rB));
						const float impulse = -_spring.MassScale * mass * (cdot + _spring.BiasRate * c) - _spring.ImpulseScale * _impulse;
						_impulse += impulse;
						applyPair(a, b, rA, rB, axis * impulse);
					}
					// 縮む限り (押し離す向きだけ)
					solveLimit(step, useBias, len - minLength, axis, rA, rB, mass, _lower, 1.0f);
					// 伸びる限り (引き寄せる向きだけ)
					solveLimit(step, useBias, maxLength - len, axis, rA, rB, mass, _upper, -1.0f);
				}
				else
				{
					// 硬い棒
					const Softness soft = useBias ? jointSoftness(Desc, step) : Softness();
					const float c = len - Desc.Length;
					const float cdot = dot(axis, relativeVelocity(a, b, rA, rB));
					const float bias = useBias ? soft.BiasRate * c : 0.0f;
					const float impulse = -soft.MassScale * mass * (cdot + bias) - soft.ImpulseScale * _impulse;
					_impulse += impulse;
					applyPair(a, b, rA, rB, axis * impulse);
				}
			}

			float Force(float invH) const override { return std::fabs(_impulse + _lower - _upper) * invH; }

		private:
			Softness _spring;
			float    _impulse = 0.0f;   // ばね / 棒
			float    _lower   = 0.0f;   // 縮む限り (0 以上)
			float    _upper   = 0.0f;   // 伸びる限り (0 以上)

			Float3 currentAxis(Float3 rA, Float3 rB, float* outLength) const
			{
				const Float3 d = (B.Body->Center() + rB) - (A.Body->Center() + rA);
				const float len = length(d);
				if (outLength) *outLength = len;
				return len > 1e-6f ? d * (1.0f / len) : Float3(0.0f, 1.0f, 0.0f);
			}

			// 片側だけの拘束 (c >= 0 を保つ)。sign : +1 なら押し離す / -1 なら引き寄せる
			void solveLimit(const SolverStep& step, bool useBias, float c, Float3 axis, Float3 rA, Float3 rB, float mass, float& accumulated, float sign)
			{
				SolverBody& a = *A.Body;
				SolverBody& b = *B.Body;
				float bias = 0.0f, massScale = 1.0f, impulseScale = 0.0f;
				if (c > 0.0f)
				{
					bias = c * step.InvH;   // まだ届いていない : その距離までは近づいてよい
				}
				else if (useBias)
				{
					const Softness soft = step.JointSoftness;
					bias = soft.BiasRate * c;
					massScale = soft.MassScale;
					impulseScale = soft.ImpulseScale;
				}
				const float cdot = sign * dot(axis, relativeVelocity(a, b, rA, rB));
				float impulse = -massScale * mass * (cdot + bias) - impulseScale * accumulated;
				const float next = (std::max)(0.0f, accumulated + impulse);
				impulse = next - accumulated;
				accumulated = next;
				applyPair(a, b, rA, rB, axis * (sign * impulse));
			}
		};

		// ==============================================================
		// Ball : 2 点を合わせる (どの向きにも回れる)
		// ==============================================================
		class BallJoint : public Joint
		{
		public:
			void Prepare(const SolverStep&) override {}

			void WarmStart() override
			{
				applyPair(*A.Body, *B.Body, anchorA(), anchorB(), _linear);
			}

			void Solve(const SolverStep& step, bool useBias) override
			{
				solvePoint(step, useBias);
			}

			float Force(float invH) const override { return length(_linear) * invH; }

		protected:
			Float3 _linear;   // 点を合わせる力の積み重ね

			void solvePoint(const SolverStep& step, bool useBias)
			{
				SolverBody& a = *A.Body;
				SolverBody& b = *B.Body;
				const Float3 rA = anchorA(), rB = anchorB();
				const Float3 c = (b.Center() + rB) - (a.Center() + rA);
				const Softness soft = useBias ? jointSoftness(Desc, step) : Softness();
				const Float3 bias = useBias ? c * soft.BiasRate : Float3();
				const Float3 cdot = relativeVelocity(a, b, rA, rB);
				const Mat3 invK = pointMass(a, b, rA, rB).Inverse();
				const Float3 impulse = (invK * (cdot + bias)) * -soft.MassScale - _linear * soft.ImpulseScale;
				_linear = _linear + impulse;
				applyPair(a, b, rA, rB, impulse);
			}
		};

		// ==============================================================
		// Fixed : 点を合わせ、向きも作った時のまま (2 つで 1 つの物のように動く)
		// ==============================================================
		class FixedJoint final : public BallJoint
		{
		public:
			explicit FixedJoint(const Quat& relative) : _relative(relative) {}

			void WarmStart() override
			{
				BallJoint::WarmStart();
				A.Body->ApplyAngularImpulse(-_angular);
				B.Body->ApplyAngularImpulse(_angular);
			}

			void Solve(const SolverStep& step, bool useBias) override
			{
				SolverBody& a = *A.Body;
				SolverBody& b = *B.Body;
				// 向き : qB qA^-1 が作った時の値になるように。ずれ = 2 × (qB qA^-1 relative^-1) のベクトル部 (小さな角)
				const Softness soft = useBias ? jointSoftness(Desc, step) : Softness();
				Float3 c;
				if (useBias)
				{
					Quat e = (b.Q * conjugate(a.Q)) * conjugate(_relative);
					if (e.w < 0.0f) e = Quat(-e.x, -e.y, -e.z, -e.w);
					c = Float3(e.x, e.y, e.z) * 2.0f;
				}
				const Float3 cdot = b.W - a.W;
				const Mat3 invK = (a.InvInertia + b.InvInertia).Inverse();
				const Float3 impulse = (invK * (cdot + c * soft.BiasRate)) * -soft.MassScale - _angular * soft.ImpulseScale;
				_angular = _angular + impulse;
				a.ApplyAngularImpulse(-impulse);
				b.ApplyAngularImpulse(impulse);

				solvePoint(step, useBias);
			}

			float Force(float invH) const override { return length(_linear) * invH; }

		private:
			Quat   _relative;
			Float3 _angular;
		};
	}

	std::unique_ptr<Joint> CreateJoint(const JointDesc& desc, const Quat& fixedRelative)
	{
		std::unique_ptr<Joint> joint;
		switch (static_cast<JointType>(desc.Type))
		{
		case JointType::Distance: joint = std::make_unique<DistanceJoint>(); break;
		case JointType::Ball:     joint = std::make_unique<BallJoint>(); break;
		case JointType::Fixed:    joint = std::make_unique<FixedJoint>(fixedRelative); break;
		default: return nullptr;
		}
		joint->Desc = desc;
		return joint;
	}
}

/************************************
* End of file
************************************/
