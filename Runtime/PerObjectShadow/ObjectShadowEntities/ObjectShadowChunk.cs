using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;

namespace UnityEngine.Rendering.Universal
{
    internal abstract class ObjectShadowChunk : IDisposable
    {
        public int count { get; protected set; }
        public int capacity { get; protected set; }

        public JobHandle currentJobHandle { get; set; }

        public virtual void Push() { count++; }
        public abstract void RemoveAtSwapBack(int entityIndex);
        public abstract void SetCapacity(int capacityIn);

        public virtual void Dispose() { }

        protected void ResizeNativeArray(ref TransformAccessArray array, PerObjectShadowProjector[] projectors, int capacityIn)
        {
            var newArray = new TransformAccessArray(capacityIn);
            if (array.isCreated)
            {
                for (int i = 0; i < array.length; ++i)
                    newArray.Add(projectors[i].transform);
                array.Dispose();
            }
            array = newArray;
        }

        protected void RemoveAtSwapBack<T>(ref NativeArray<T> array, int index, int countIn) where T : struct
        {
            array[index] = array[countIn - 1];
        }

        protected void RemoveAtSwapBack<T>(ref T[] array, int index, int countIn)
        {
            array[index] = array[countIn - 1];
        }
    }

}
