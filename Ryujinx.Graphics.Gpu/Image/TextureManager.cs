﻿using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Gpu.Engine.Types;
using System;

namespace Ryujinx.Graphics.Gpu.Image
{
    /// <summary>
    /// Texture manager.
    /// </summary>
    class TextureManager : IDisposable
    {
        private readonly GpuContext _context;

        private readonly TextureBindingsManager _cpBindingsManager;
        private readonly TextureBindingsManager _gpBindingsManager;

        private readonly Texture[] _rtColors;
        private readonly ITexture[] _rtHostColors;
        private Texture _rtDepthStencil;
        private ITexture _rtHostDs;

        public int ClipRegionWidth { get; private set; }
        public int ClipRegionHeight { get; private set; }

        /// <summary>
        /// The scaling factor applied to all currently bound render targets.
        /// </summary>
        public float RenderTargetScale { get; private set; } = 1f;

        /// <summary>
        /// Creates a new instance of the texture manager.
        /// </summary>
        /// <param name="context">GPU context that the texture manager belongs to</param>
        /// <param name="channel">GPU channel that the texture manager belongs to</param>
        public TextureManager(GpuContext context, GpuChannel channel)
        {
            _context = context;

            TexturePoolCache texturePoolCache = new TexturePoolCache(context);

            float[] scales = new float[64];
            new Span<float>(scales).Fill(1f);

            _cpBindingsManager = new TextureBindingsManager(context, channel, texturePoolCache, scales, isCompute: true);
            _gpBindingsManager = new TextureBindingsManager(context, channel, texturePoolCache, scales, isCompute: false);

            _rtColors = new Texture[Constants.TotalRenderTargets];
            _rtHostColors = new ITexture[Constants.TotalRenderTargets];
        }

        /// <summary>
        /// Rents the texture bindings array of the compute pipeline.
        /// </summary>
        /// <param name="count">The number of bindings needed</param>
        /// <returns>The texture bindings array</returns>
        public TextureBindingInfo[] RentComputeTextureBindings(int count)
        {
            return _cpBindingsManager.RentTextureBindings(0, count);
        }

        /// <summary>
        /// Rents the texture bindings array for a given stage on the graphics pipeline.
        /// </summary>
        /// <param name="stage">The index of the shader stage to bind the textures</param>
        /// <param name="count">The number of bindings needed</param>
        /// <returns>The texture bindings array</returns>
        public TextureBindingInfo[] RentGraphicsTextureBindings(int stage, int count)
        {
            return _gpBindingsManager.RentTextureBindings(stage, count);
        }

        /// <summary>
        /// Rents the image bindings array of the compute pipeline.
        /// </summary>
        /// <param name="count">The number of bindings needed</param>
        /// <returns>The image bindings array</returns>
        public TextureBindingInfo[] RentComputeImageBindings(int count)
        {
            return _cpBindingsManager.RentImageBindings(0, count);
        }

        /// <summary>
        /// Rents the image bindings array for a given stage on the graphics pipeline.
        /// </summary>
        /// <param name="stage">The index of the shader stage to bind the images</param>
        /// <param name="count">The number of bindings needed</param>
        /// <returns>The image bindings array</returns>
        public TextureBindingInfo[] RentGraphicsImageBindings(int stage, int count)
        {
            return _gpBindingsManager.RentImageBindings(stage, count);
        }

        /// <summary>
        /// Sets the texture constant buffer index on the compute pipeline.
        /// </summary>
        /// <param name="index">The texture constant buffer index</param>
        public void SetComputeTextureBufferIndex(int index)
        {
            _cpBindingsManager.SetTextureBufferIndex(index);
        }

        /// <summary>
        /// Sets the texture constant buffer index on the graphics pipeline.
        /// </summary>
        /// <param name="index">The texture constant buffer index</param>
        public void SetGraphicsTextureBufferIndex(int index)
        {
            _gpBindingsManager.SetTextureBufferIndex(index);
        }

        /// <summary>
        /// Sets the current sampler pool on the compute pipeline.
        /// </summary>
        /// <param name="gpuVa">The start GPU virtual address of the sampler pool</param>
        /// <param name="maximumId">The maximum ID of the sampler pool</param>
        /// <param name="samplerIndex">The indexing type of the sampler pool</param>
        public void SetComputeSamplerPool(ulong gpuVa, int maximumId, SamplerIndex samplerIndex)
        {
            _cpBindingsManager.SetSamplerPool(gpuVa, maximumId, samplerIndex);
        }

        /// <summary>
        /// Sets the current sampler pool on the graphics pipeline.
        /// </summary>
        /// <param name="gpuVa">The start GPU virtual address of the sampler pool</param>
        /// <param name="maximumId">The maximum ID of the sampler pool</param>
        /// <param name="samplerIndex">The indexing type of the sampler pool</param>
        public void SetGraphicsSamplerPool(ulong gpuVa, int maximumId, SamplerIndex samplerIndex)
        {
            _gpBindingsManager.SetSamplerPool(gpuVa, maximumId, samplerIndex);
        }

        /// <summary>
        /// Sets the current texture pool on the compute pipeline.
        /// </summary>
        /// <param name="gpuVa">The start GPU virtual address of the texture pool</param>
        /// <param name="maximumId">The maximum ID of the texture pool</param>
        public void SetComputeTexturePool(ulong gpuVa, int maximumId)
        {
            _cpBindingsManager.SetTexturePool(gpuVa, maximumId);
        }

        /// <summary>
        /// Sets the current texture pool on the graphics pipeline.
        /// </summary>
        /// <param name="gpuVa">The start GPU virtual address of the texture pool</param>
        /// <param name="maximumId">The maximum ID of the texture pool</param>
        public void SetGraphicsTexturePool(ulong gpuVa, int maximumId)
        {
            _gpBindingsManager.SetTexturePool(gpuVa, maximumId);
        }

        /// <summary>
        /// Check if a texture's scale must be updated to match the configured resolution scale.
        /// </summary>
        /// <param name="texture">The texture to check</param>
        /// <returns>True if the scale needs updating, false if the scale is up to date</returns>
        private bool ScaleNeedsUpdated(Texture texture)
        {
            return texture != null && !(texture.ScaleMode == TextureScaleMode.Blacklisted || texture.ScaleMode == TextureScaleMode.Undesired) && texture.ScaleFactor != GraphicsConfig.ResScale;
        }

        /// <summary>
        /// Sets the render target color buffer.
        /// </summary>
        /// <param name="index">The index of the color buffer to set (up to 8)</param>
        /// <param name="color">The color buffer texture</param>
        /// <returns>True if render target scale must be updated.</returns>
        public bool SetRenderTargetColor(int index, Texture color)
        {
            bool hasValue = color != null;
            bool changesScale = (hasValue != (_rtColors[index] != null)) || (hasValue && RenderTargetScale != color.ScaleFactor);

            if (_rtColors[index] != color)
            {
                _rtColors[index]?.SignalModifying(false);

                if (color != null)
                {
                    color.SynchronizeMemory();
                    color.SignalModifying(true);
                }

                _rtColors[index] = color;
            }

            return changesScale || ScaleNeedsUpdated(color);
        }

        /// <summary>
        /// Sets the render target depth-stencil buffer.
        /// </summary>
        /// <param name="depthStencil">The depth-stencil buffer texture</param>
        /// <returns>True if render target scale must be updated.</returns>
        public bool SetRenderTargetDepthStencil(Texture depthStencil)
        {
            bool hasValue = depthStencil != null;
            bool changesScale = (hasValue != (_rtDepthStencil != null)) || (hasValue && RenderTargetScale != depthStencil.ScaleFactor);

            if (_rtDepthStencil != depthStencil)
            {
                _rtDepthStencil?.SignalModifying(false);

                if (depthStencil != null)
                {
                    depthStencil.SynchronizeMemory();
                    depthStencil.SignalModifying(true);
                }

                _rtDepthStencil = depthStencil;
            }

            return changesScale || ScaleNeedsUpdated(depthStencil);
        }

        /// <summary>
        /// Sets the host clip region, which should be the intersection of all render target texture sizes.
        /// </summary>
        /// <param name="width">Width of the clip region, defined as the minimum width across all bound textures</param>
        /// <param name="height">Height of the clip region, defined as the minimum height across all bound textures</param>
        public void SetClipRegion(int width, int height)
        {
            ClipRegionWidth = width;
            ClipRegionHeight = height;
        }

        /// <summary>
        /// Gets the first available bound colour target, or the depth stencil target if not present.
        /// </summary>
        /// <returns>The first bound colour target, otherwise the depth stencil target</returns>
        public Texture GetAnyRenderTarget()
        {
            return _rtColors[0] ?? _rtDepthStencil;
        }

        /// <summary>
        /// Updates the Render Target scale, given the currently bound render targets.
        /// This will update scale to match the configured scale, scale textures that are eligible but not scaled,
        /// and propagate blacklisted status from one texture to the ones bound with it.
        /// </summary>
        /// <param name="singleUse">If this is not -1, it indicates that only the given indexed target will be used.</param>
        public void UpdateRenderTargetScale(int singleUse)
        {
            // Make sure all scales for render targets are at the highest they should be. Blacklisted targets should propagate their scale to the other targets.
            bool mismatch = false;
            bool blacklisted = false;
            bool hasUpscaled = false;
            bool hasUndesired = false;
            float targetScale = GraphicsConfig.ResScale;

            void ConsiderTarget(Texture target)
            {
                if (target == null) return;
                float scale = target.ScaleFactor;

                switch (target.ScaleMode)
                {
                    case TextureScaleMode.Blacklisted:
                        mismatch |= scale != 1f;
                        blacklisted = true;
                        break;
                    case TextureScaleMode.Eligible:
                        mismatch = true; // We must make a decision.
                        break;
                    case TextureScaleMode.Undesired:
                        hasUndesired = true;
                        mismatch |= scale != 1f || hasUpscaled; // If another target is upscaled, scale this one up too.
                        break;
                    case TextureScaleMode.Scaled:
                        hasUpscaled = true;
                        mismatch |= hasUndesired || scale != targetScale; // If the target scale has changed, reset the scale for all targets.
                        break;
                }
            }

            if (singleUse != -1)
            {
                // If only one target is in use (by a clear, for example) the others do not need to be checked for mismatching scale.
                ConsiderTarget(_rtColors[singleUse]);
            }
            else
            {
                foreach (Texture color in _rtColors)
                {
                    ConsiderTarget(color);
                }
            }

            ConsiderTarget(_rtDepthStencil);

            mismatch |= blacklisted && hasUpscaled;

            if (blacklisted || (hasUndesired && !hasUpscaled))
            {
                targetScale = 1f;
            }

            if (mismatch)
            {
                if (blacklisted)
                {
                    // Propagate the blacklisted state to the other textures.
                    foreach (Texture color in _rtColors)
                    {
                        color?.BlacklistScale();
                    }

                    _rtDepthStencil?.BlacklistScale();
                }
                else
                {
                    // Set the scale of the other textures.
                    foreach (Texture color in _rtColors)
                    {
                        color?.SetScale(targetScale);
                    }

                    _rtDepthStencil?.SetScale(targetScale);
                }
            }

            RenderTargetScale = targetScale;
        }

        /// <summary>
        /// Gets a texture and a sampler from their respective pools from a texture ID and a sampler ID.
        /// </summary>
        /// <param name="textureId">ID of the texture</param>
        /// <param name="samplerId">ID of the sampler</param>
        public (Texture, Sampler) GetGraphicsTextureAndSampler(int textureId, int samplerId)
        {
            return _gpBindingsManager.GetTextureAndSampler(textureId, samplerId);
        }

        /// <summary>
        /// Commits bindings on the compute pipeline.
        /// </summary>
        public void CommitComputeBindings()
        {
            // Every time we switch between graphics and compute work,
            // we must rebind everything.
            // Since compute work happens less often, we always do that
            // before and after the compute dispatch.
            _cpBindingsManager.Rebind();
            _cpBindingsManager.CommitBindings();
            _gpBindingsManager.Rebind();
        }

        /// <summary>
        /// Commits bindings on the graphics pipeline.
        /// </summary>
        public void CommitGraphicsBindings()
        {
            _gpBindingsManager.CommitBindings();

            UpdateRenderTargets();
        }

        /// <summary>
        /// Gets a texture descriptor used on the compute pipeline.
        /// </summary>
        /// <param name="poolGpuVa">GPU virtual address of the texture pool</param>
        /// <param name="bufferIndex">Index of the constant buffer with texture handles</param>
        /// <param name="maximumId">Maximum ID of the texture pool</param>
        /// <param name="handle">Shader "fake" handle of the texture</param>
        /// <param name="cbufSlot">Shader constant buffer slot of the texture</param>
        /// <returns>The texture descriptor</returns>
        public TextureDescriptor GetComputeTextureDescriptor(ulong poolGpuVa, int bufferIndex, int maximumId, int handle, int cbufSlot)
        {
            return _cpBindingsManager.GetTextureDescriptor(poolGpuVa, bufferIndex, maximumId, 0, handle, cbufSlot);
        }

        /// <summary>
        /// Gets a texture descriptor used on the graphics pipeline.
        /// </summary>
        /// <param name="poolGpuVa">GPU virtual address of the texture pool</param>
        /// <param name="bufferIndex">Index of the constant buffer with texture handles</param>
        /// <param name="maximumId">Maximum ID of the texture pool</param>
        /// <param name="stageIndex">Index of the shader stage where the texture is bound</param>
        /// <param name="handle">Shader "fake" handle of the texture</param>
        /// <param name="cbufSlot">Shader constant buffer slot of the texture</param>
        /// <returns>The texture descriptor</returns>
        public TextureDescriptor GetGraphicsTextureDescriptor(
            ulong poolGpuVa,
            int bufferIndex,
            int maximumId,
            int stageIndex,
            int handle,
            int cbufSlot)
        {
            return _gpBindingsManager.GetTextureDescriptor(poolGpuVa, bufferIndex, maximumId, stageIndex, handle, cbufSlot);
        }

        /// <summary>
        /// Update host framebuffer attachments based on currently bound render target buffers.
        /// </summary>
        public void UpdateRenderTargets()
        {
            bool anyChanged = false;

            if (_rtHostDs != _rtDepthStencil?.HostTexture)
            {
                _rtHostDs = _rtDepthStencil?.HostTexture;

                anyChanged = true;
            }

            for (int index = 0; index < _rtColors.Length; index++)
            {
                ITexture hostTexture = _rtColors[index]?.HostTexture;

                if (_rtHostColors[index] != hostTexture)
                {
                    _rtHostColors[index] = hostTexture;

                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                _context.Renderer.Pipeline.SetRenderTargets(_rtHostColors, _rtHostDs);
            }
        }

        /// <summary>
        /// Update host framebuffer attachments based on currently bound render target buffers.
        /// </summary>
        /// <remarks>
        /// All attachments other than <paramref name="index"/> will be unbound.
        /// </remarks>
        /// <param name="index">Index of the render target color to be updated</param>
        public void UpdateRenderTarget(int index)
        {
            new Span<ITexture>(_rtHostColors).Fill(null);
            _rtHostColors[index] = _rtColors[index]?.HostTexture;

            _context.Renderer.Pipeline.SetRenderTargets(_rtHostColors, null);
        }

        /// <summary>
        /// Update host framebuffer attachments based on currently bound render target buffers.
        /// </summary>
        /// <remarks>
        /// All color attachments will be unbound.
        /// </remarks>
        public void UpdateRenderTargetDepthStencil()
        {
            new Span<ITexture>(_rtHostColors).Fill(null);
            _rtHostDs = _rtDepthStencil?.HostTexture;

            _context.Renderer.Pipeline.SetRenderTargets(_rtHostColors, _rtHostDs);
        }

        /// <summary>
        /// Forces all textures, samplers, images and render targets to be rebound the next time
        /// CommitGraphicsBindings is called.
        /// </summary>
        public void Rebind()
        {
            _gpBindingsManager.Rebind();

            for (int index = 0; index < _rtHostColors.Length; index++)
            {
                _rtHostColors[index] = null;
            }

            _rtHostDs = null;
        }

        /// <summary>
        /// Disposes the texture manager.
        /// It's an error to use the texture manager after disposal.
        /// </summary>
        public void Dispose()
        {
            _cpBindingsManager.Dispose();
            _gpBindingsManager.Dispose();

            for (int i = 0; i < _rtColors.Length; i++)
            {
                _rtColors[i]?.DecrementReferenceCount();
                _rtColors[i] = null;
            }

            _rtDepthStencil?.DecrementReferenceCount();
            _rtDepthStencil = null;
        }
    }
}
