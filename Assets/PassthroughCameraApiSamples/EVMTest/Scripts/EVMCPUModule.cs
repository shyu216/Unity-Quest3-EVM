/*
 * Eulerian Video Magnification (EVM) implementation in C# using UnityEngine.
 *
 * This implementation performs spatial decomposition using a Gaussian pyramid
 * and temporal filtering using a Butterworth filter.
 *
 * Optimized for facial pulse detection applications.
 *
 * Author: SIHONG YU
 * Date: 2025.6
 */

using UnityEngine;

namespace PassthroughCameraSamples.EVMTest
{
    public class EvmMagnifier
    {
        private double m_alpha;
        private int m_nLevels;
        private Texture2D[] m_lowpass1;
        private Texture2D[] m_lowpass2;
        private Texture2D[] m_prevPyr;
        private double[] m_lowA;
        private double[] m_lowB;
        private double[] m_highA;
        private double[] m_highB;

        public EvmMagnifier(double alpha = 50, double fl = 60 / 60.0, double fh = 100 / 60.0, int nLevels = 4, int fps = 30)
        {
            m_alpha = alpha;
            m_nLevels = nLevels;
            m_lowpass1 = null;
            m_lowpass2 = null;
            m_prevPyr = null;

            var (lowA, lowB) = ButterworthHelper.LowPass(1, fl / fps);
            var (highA, highB) = ButterworthHelper.LowPass(1, fh / fps);
            m_lowA = lowA;
            m_lowB = lowB;
            m_highA = highA;
            m_highB = highB;
        }

        public Texture2D[] BuildGaussianPyramid(Texture2D image)
        {
            Debug.Log($"Building Gaussian Pyramid: Width={image.width}, Height={image.height}");
            var gaussianPyramid = new Texture2D[m_nLevels];
            gaussianPyramid[0] = image;
            for (var i = 1; i < m_nLevels; i++)
            {
                var downsampled = Downsample(image);
                Debug.Log($"Downsampled Level {i}: Width={downsampled.width}, Height={downsampled.height}");
                gaussianPyramid[i] = downsampled;
                image = downsampled;
            }
            return gaussianPyramid;
        }

        private Texture2D Downsample(Texture2D image)
        {
            var width = image.width / 2;
            var height = image.height / 2;
            var downsampled = new Texture2D(width, height);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var avgColor = (image.GetPixel(x * 2, y * 2) + image.GetPixel(x * 2 + 1, y * 2) +
                                    image.GetPixel(x * 2, y * 2 + 1) + image.GetPixel(x * 2 + 1, y * 2 + 1)) / 4;
                    downsampled.SetPixel(x, y, avgColor);
                }
            }
            downsampled.Apply();
            return downsampled;
        }

        public Texture2D[] ApplyButterFilter(Texture2D[] pyr)
        {
            Debug.Log($"Applying Butter Filter: Levels={pyr.Length}");
            if (m_lowpass1 == null || m_lowpass2 == null || m_prevPyr == null)
            {
                m_lowpass1 = new Texture2D[m_nLevels];
                m_lowpass2 = new Texture2D[m_nLevels];
                m_prevPyr = new Texture2D[m_nLevels];

                for (var i = 0; i < m_nLevels; i++)
                {
                    m_lowpass1[i] = new Texture2D(pyr[i].width, pyr[i].height);
                    m_lowpass2[i] = new Texture2D(pyr[i].width, pyr[i].height);
                    m_prevPyr[i] = new Texture2D(pyr[i].width, pyr[i].height);
                }
            }

            var filtered = new Texture2D[m_nLevels];
            for (var i = 0; i < m_nLevels; i++)
            {
                Debug.Log($"Filtering Level {i}: Width={pyr[i].width}, Height={pyr[i].height}");
                var tempPyr = pyr[i];
                var filtered1 = ApplyLowPassFilter(tempPyr, m_lowpass1[i], m_prevPyr[i], m_highA, m_highB);
                var filtered2 = ApplyLowPassFilter(tempPyr, m_lowpass2[i], m_prevPyr[i], m_lowA, m_lowB);

                m_prevPyr[i] = tempPyr;

                filtered[i] = SubtractTextures(filtered1, filtered2);
            }

            return filtered;
        }

        private Texture2D ApplyLowPassFilter(Texture2D input, Texture2D lowpass, Texture2D prev, double[] a, double[] b)
        {
            for (var y = 0; y < lowpass.height; y++)
            {
                for (var x = 0; x < lowpass.width; x++)
                {
                    var tempColor = input.GetPixel(x, y);
                    var lowpassColor = lowpass.GetPixel(x, y);
                    var prevColor = prev.GetPixel(x, y);

                    var lowPassR = -b[1] * lowpassColor.r + a[0] * tempColor.r + a[1] * prevColor.r;
                    var lowPassG = -b[1] * lowpassColor.g + a[0] * tempColor.g + a[1] * prevColor.g;
                    var lowPassB = -b[1] * lowpassColor.b + a[0] * tempColor.b + a[1] * prevColor.b;

                    lowpass.SetPixel(x, y, new Color((float)(lowPassR / b[0]), (float)(lowPassG / b[0]), (float)(lowPassB / b[0])));
                }
            }
            lowpass.Apply();
            return lowpass;
        }

        private Texture2D SubtractTextures(Texture2D tex1, Texture2D tex2)
        {
            var result = new Texture2D(tex1.width, tex1.height);
            for (var y = 0; y < tex1.height; y++)
            {
                for (var x = 0; x < tex1.width; x++)
                {
                    var color = tex1.GetPixel(x, y) - tex2.GetPixel(x, y);
                    result.SetPixel(x, y, color);
                }
            }
            result.Apply();
            return result;
        }

        public Texture2D[] AmplifyPyramid(Texture2D[] filtered)
        {
            foreach (var level in filtered)
            {
                AmplifyTexture(level);
            }
            return filtered;
        }

        private void AmplifyTexture(Texture2D texture)
        {
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    var color = texture.GetPixel(x, y) * (float)m_alpha;
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
        }

        public Texture2D ReconstructPyramid(Texture2D[] filtered)
        {
            Debug.Log($"Reconstructing Pyramid: Levels={filtered.Length}");
            var upsampled = filtered[0];
            for (var l = 1; l < m_nLevels; l++)
            {
                Debug.Log($"Upsampling Level {l}: Width={filtered[l].width}, Height={filtered[l].height}");
                upsampled = Upsample(upsampled, filtered[l].width, filtered[l].height);
                AddTextures(upsampled, filtered[l]);
            }

            DivideTexture(upsampled, m_nLevels);
            Debug.Log($"Final Reconstructed Texture: Width={upsampled.width}, Height={upsampled.height}");

            return upsampled;
        }

        private Texture2D Upsample(Texture2D texture, int width, int height)
        {
            var upsampled = new Texture2D(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var color = texture.GetPixel(x / 2, y / 2);
                    upsampled.SetPixel(x, y, color);
                }
            }
            upsampled.Apply();
            return upsampled;
        }

        private void AddTextures(Texture2D tex1, Texture2D tex2)
        {
            for (var y = 0; y < tex1.height; y++)
            {
                for (var x = 0; x < tex1.width; x++)
                {
                    var color = tex1.GetPixel(x, y) + tex2.GetPixel(x, y);
                    tex1.SetPixel(x, y, color);
                }
            }
            tex1.Apply();
        }

        private void DivideTexture(Texture2D texture, int divisor)
        {
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    var color = texture.GetPixel(x, y) / divisor;
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
        }

        public void Reset()
        {
            m_lowpass1 = null;
            m_lowpass2 = null;
        }

        public Texture2D ProcessFrame(Texture2D frame)
        {
            Debug.Log($"Processing Frame: Width={frame.width}, Height={frame.height}");
            var pyramid = BuildGaussianPyramid(frame);
            var filtered = ApplyButterFilter(pyramid);
            var amplified = AmplifyPyramid(filtered);
            var upsampled = ReconstructPyramid(amplified);

            AddTextures(frame, upsampled);
            ClampTexture(frame);

            Debug.Log($"Processed Frame: Width={frame.width}, Height={frame.height}");
            return frame;
        }

        private void ClampTexture(Texture2D texture)
        {
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    var color = texture.GetPixel(x, y);
                    color.r = Mathf.Clamp(color.r, 0, 1);
                    color.g = Mathf.Clamp(color.g, 0, 1);
                    color.b = Mathf.Clamp(color.b, 0, 1);
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
        }
    }
}