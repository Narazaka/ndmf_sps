using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace com.meronmks.ndmfsps
{
    /// <summary>
    /// Direct BlendTree内でAAP (Animator As Property) を使った演算を行うユーティリティ。
    /// VRCFury v1.1001.0のMathServiceに相当する軽量版。
    /// </summary>
    internal class AapMath
    {
        private readonly AnimatorController _controller;
        private readonly BlendTree _directTree;
        private readonly HashSet<string> _registeredParams = new HashSet<string>();

        private const string AlwaysOneParam = "__ndmfsps_one";

        public BlendTree DirectTree => _directTree;

        public AapMath(AnimatorController controller)
        {
            _controller = controller;
            _directTree = new BlendTree
            {
                name = "DBT",
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false
            };
            EnsureParam(AlwaysOneParam, 1f);
        }

        public void EnsureParam(string name, float defaultValue = 0f)
        {
            if (!_registeredParams.Add(name)) return;
            _controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = defaultValue
            });
        }

        public string MakeAap(string name, float defaultValue = 0f)
        {
            EnsureParam(name, defaultValue);
            AddDirect(AlwaysOneParam, MakeSetterClip(name, 0f));
            return name;
        }

        public AnimationClip MakeSetterClip(string paramName, float value)
        {
            var clip = new AnimationClip { name = $"AAP {paramName}={value}" };
            var curve = new AnimationCurve(new Keyframe(0f, value));
            clip.SetCurve("", typeof(Animator), paramName, curve);
            return clip;
        }

        public void AddDirect(string blendParam, Motion motion)
        {
            _directTree.AddChild(motion);
            var children = _directTree.children;
            var child = children[children.Length - 1];
            child.directBlendParameter = blendParam;
            children[children.Length - 1] = child;
            _directTree.children = children;
        }

        public void AddDirect(Motion motion)
        {
            AddDirect(AlwaysOneParam, motion);
        }

        public BlendTree Make1D(string name, string blendParam, params (float threshold, Motion motion)[] children)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = blendParam,
                useAutomaticThresholds = false
            };
            foreach (var (threshold, motion) in children)
            {
                tree.AddChild(motion ?? new AnimationClip(), threshold);
            }
            return tree;
        }

        public string Map(string outputName, string inputParam, float inMin, float inMax, float outMin, float outMax)
        {
            EnsureParam(inputParam);
            var output = MakeAap(outputName);

            var minClip = MakeSetterClip(output, outMin);
            var maxClip = MakeSetterClip(output, outMax);

            BlendTree tree;
            if (inMin < inMax)
            {
                tree = Make1D($"{inputParam} ({inMin}-{inMax}) -> ({outMin}-{outMax})", inputParam,
                    (inMin, minClip),
                    (inMax, maxClip));
            }
            else
            {
                tree = Make1D($"{inputParam} ({inMax}-{inMin}) -> ({outMax}-{outMin})", inputParam,
                    (inMax, maxClip),
                    (inMin, minClip));
            }
            AddDirect(tree);
            return output;
        }

        public delegate Motion ConditionFactory(Motion whenTrue, Motion whenFalse);

        public ConditionFactory GreaterThan(string paramA, float threshold, bool orEqual = false)
        {
            EnsureParam(paramA);
            var belowThreshold = orEqual ? threshold - 0.00001f : threshold;
            var aboveThreshold = orEqual ? threshold : threshold + 0.00001f;
            return (whenTrue, whenFalse) => Make1D(
                $"{paramA} {(orEqual ? ">=" : ">")} {threshold}",
                paramA,
                (belowThreshold, whenFalse ?? new AnimationClip()),
                (aboveThreshold, whenTrue ?? new AnimationClip()));
        }

        public void SetValueWithConditions(params (Motion motion, ConditionFactory condition)[] targets)
        {
            Motion elseTree = null;
            foreach (var (motion, condition) in targets.Reverse())
            {
                if (condition == null)
                {
                    elseTree = motion;
                    continue;
                }
                elseTree = condition(motion, elseTree);
            }
            if (elseTree != null) AddDirect(elseTree);
        }

        public Motion MakeCopier(string fromParam, string toParam)
        {
            EnsureParam(fromParam);
            var subTree = new BlendTree
            {
                name = $"Copy {fromParam} -> {toParam}",
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false
            };
            var setterClip = MakeSetterClip(toParam, 1f);
            subTree.AddChild(setterClip);
            var children = subTree.children;
            var child = children[0];
            child.directBlendParameter = fromParam;
            children[0] = child;
            subTree.children = children;
            return subTree;
        }

        public Motion MakeConstSetter(string toParam, float value)
        {
            return MakeSetterClip(toParam, value);
        }
    }
}
