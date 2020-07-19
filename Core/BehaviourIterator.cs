﻿using System;
using System.Collections.Generic;

namespace Bonsai.Core
{
  /// <summary>
  /// A special iterator to handle traversing a behaviour tree.
  /// </summary>
  public sealed class BehaviourIterator
  {
    // Keeps track of the traversal path.
    // Useful to help on aborts and interrupts.
    private readonly Utility.FixedSizeStack<int> traversal;
    private readonly Utility.FixedSizeStack<int> branchTicks;

    // Access to the tree so we can find any node from pre-order index.
    private readonly BehaviourTree tree;
    private readonly Queue<int> requestedTraversals;

    /// <summary>
    /// Called when the iterators finishes iterating the entire tree.
    /// </summary>
    public Action OnDone = delegate { };

    public BehaviourIterator(BehaviourTree tree, int levelOffset)
    {
      this.tree = tree;

      // Since tree heights starts from zero, the stack needs to have treeHeight + 1 slots.
      int maxTraversalLength = this.tree.Height + 1;
      traversal = new Utility.FixedSizeStack<int>(maxTraversalLength);
      branchTicks = new Utility.FixedSizeStack<int>(maxTraversalLength);
      requestedTraversals = new Queue<int>(maxTraversalLength);

      LevelOffset = levelOffset;
    }

    /// <summary>
    /// Ticks the iterator.
    /// </summary>
    public void Update()
    {
      CallOnEnterOnQueuedNodes();
      TickBranch();

      int index = traversal.Peek();
      BehaviourNode node = tree.allNodes[index];
      LastStatusReturned = node.Run();

#if UNITY_EDITOR
      node.SetStatusEditor(LastStatusReturned);
#endif

      if (LastStatusReturned != BehaviourNode.Status.Running)
      {
        PopNode();
        CallOnChildExit(node);
      }

      if (traversal.Count == 0)
      {
        OnDone();
      }
    }

    private void TickBranch()
    {
      for (int i = 0; i < branchTicks.Count; i++)
      {
        int nodeIndex = branchTicks.GetValue(i);
        tree.allNodes[nodeIndex].OnBranchTick();
      }
    }

    private void CallOnEnterOnQueuedNodes()
    {
      // Make sure to call on enter on any queued new traversals.
      while (requestedTraversals.Count != 0)
      {
        int i = requestedTraversals.Dequeue();
        BehaviourNode node = tree.allNodes[i];
        node.OnEnter();

        if (node.CanTickOnBranch())
        {
          branchTicks.Push(i);
        }

        CallOnChildEnter(node);
      }
    }

    private void CallOnChildEnter(BehaviourNode node)
    {
      if (node.Parent)
      {
        node.Parent.OnChildEnter(node._indexOrder);
      }
    }

    private void CallOnChildExit(BehaviourNode node)
    {
      if (node.Parent)
      {
        node.Parent.OnChildExit(node._indexOrder, LastStatusReturned);
      }
    }

    /// <summary>
    /// Requests the iterator to traverse a new node.
    /// </summary>
    /// <param name="next"></param>
    public void Traverse(BehaviourNode next)
    {
      int index = next.preOrderIndex;
      traversal.Push(index);
      requestedTraversals.Enqueue(index);

      LastStatusReturned = BehaviourNode.Status.Running;

#if UNITY_EDITOR
      next.SetStatusEditor(BehaviourNode.Status.Running);
#endif
    }

    /// <summary>
    /// Tells the iterator to abort the current running subtree and jump to the aborter.
    /// </summary>
    /// <param name="aborter"></param>
    public void OnAbort(ConditionalAbort aborter)
    {
      BehaviourNode parent = aborter.Parent;
      int terminatingIndex = BehaviourNode.kInvalidOrder;

      if (parent)
      {
        terminatingIndex = parent.preOrderIndex;
      }

      // If an abort node is the root, then we need to empty the entire traversal.
      // We can achieve this by setting the terminating index to the invalid index, which is an invalid index
      // and will empty the traversal.
      while (traversal.Count != 0 && traversal.Peek() != terminatingIndex)
      {
        StepBackAbort();
      }

      // Only composite nodes need to worry about which of their subtrees fired an abort.
      if (parent.MaxChildCount() > 1)
      {
        parent.OnAbort(aborter);
      }

      // Any requested traversals are cancelled on abort.
      requestedTraversals.Clear();

      Traverse(aborter);
    }

    /// <summary>
    /// Gets the subtree that is running under a parent.
    /// This does not work directly under parallel nodes since they use their own iterator.
    /// </summary>
    /// <param name="parent"></param>
    /// <returns></returns>
    public BehaviourNode GetRunningSubtree(BehaviourNode parent)
    {
      int parentIndexInTraversal = GetIndexInTraversal(parent);
      int subtreeIndexInTraversal = parentIndexInTraversal + 1;

      int subtreePreOrder = traversal.GetValue(subtreeIndexInTraversal);
      return tree.allNodes[subtreePreOrder];
    }

    /// <summary>
    /// Gets the position of the node in the traversal stack.
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public int GetIndexInTraversal(BehaviourNode node)
    {
      return node.levelOrder - LevelOffset;
    }

    public bool IsRunning
    {
      get { return traversal.Count != 0; }
    }

    /// <summary>
    /// Gets the pre-order index of the node at the top of the traversal stack.
    /// If the iterator is not traversing anything, -1 is returned.
    /// </summary>
    public int CurrentIndex
    {
      get { return traversal.Count == 0 ? -1 : traversal.Peek(); }
    }

    public int LevelOffset { get; }

    /// <summary>
    /// The last status stored by the iterator. Can be used by composites and decorators
    /// to find out what the child returned.
    /// </summary>
    public BehaviourNode.Status LastStatusReturned { get; private set; }

    private void StepBackAbort()
    {
      var node = PopNode();

#if UNITY_EDITOR
      node.SetStatusEditor(BehaviourNode.StatusEditor.Aborted);
#endif
    }

    /// <summary>
    /// Only interrupts the subtree until a parallel node.
    /// </summary>
    /// <param name="subtree"></param>
    internal void StepBackInterrupt(BehaviourNode subtree, bool bFullInterrupt = false)
    {
      while (traversal.Count != 0 && traversal.Peek() != subtree.preOrderIndex)
      {
        var node = PopNode();

#if UNITY_EDITOR
        node.SetStatusEditor(BehaviourNode.StatusEditor.Interruption);
#endif

      }

      if (bFullInterrupt && traversal.Count != 0)
      {
        var node = PopNode();

#if UNITY_EDITOR
        node.SetStatusEditor(BehaviourNode.StatusEditor.Interruption);
#endif

        // Any requested traversals are cancelled on interruption.
        requestedTraversals.Clear();
      }
    }

    /// <summary>
    /// Gets the pre-order index of the node at the beginning of the traversal stack.
    /// </summary>
    public int FirstInTraversal
    {
      get { return traversal.GetValue(0); }
    }

    private BehaviourNode PopNode()
    {
      int index = traversal.Pop();
      BehaviourNode node = tree.allNodes[index];
      node.OnExit();

      // Guard against empty branch tick pop.
      // This could occur if a node was aborted then interrupted in succession.
      // TODO: Test this further.
      if (branchTicks.Count != 0 && node.CanTickOnBranch())
      {
        branchTicks.Pop();
      }

      return node;
    }
  }
}