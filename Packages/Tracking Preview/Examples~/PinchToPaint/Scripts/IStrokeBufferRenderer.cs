/******************************************************************************
 * Copyright (C) Ultraleap, Inc. 2011-2022.                                   *
 *                                                                            *
 * Use subject to the terms of the Apache License 2.0 available at            *
 * http://www.apache.org/licenses/LICENSE-2.0, or another agreement           *
 * between Ultraleap and you, your company or other organization.             *
 ******************************************************************************/

namespace Leap.Unity.Preview
{

    public interface IStrokeBufferRenderer
    {

        void InitializeRenderer();
        void RefreshRenderer(RingBuffer<StrokePoint> strokeBuffer);
        void StopRenderer();

    }

}