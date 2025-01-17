﻿//----------------------------------------------------------------------------
//  Copyright (C) 2004-2017 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Features2D;
using Emgu.CV.Flann;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace VideoSurveilance
{
    public static class DrawMatches
    {
        public static void FindMatch(Mat modelImage, Mat observedImage, out long matchTime, out VectorOfKeyPoint modelKeyPoints, out VectorOfKeyPoint observedKeyPoints, VectorOfVectorOfDMatch matches, out Mat mask, out Mat homography, out long score)
        {
            int k = 2;
            double uniquenessThreshold = 0.80;

            Stopwatch watch;
            homography = null;

            modelKeyPoints = new VectorOfKeyPoint();
            observedKeyPoints = new VectorOfKeyPoint();

            using (UMat uModelImage = modelImage.GetUMat(AccessType.Read))
            using (UMat uObservedImage = observedImage.GetUMat(AccessType.Read))
            {
                KAZE featureDetector = new KAZE();

                //extract features from the object image
                Mat modelDescriptors = new Mat();
                featureDetector.DetectAndCompute(uModelImage, null, modelKeyPoints, modelDescriptors, false);

                watch = Stopwatch.StartNew();

                // extract features from the observed image
                Mat observedDescriptors = new Mat();
                featureDetector.DetectAndCompute(uObservedImage, null, observedKeyPoints, observedDescriptors, false);

                // Bruteforce, slower but more accurate
                // You can use KDTree for faster matching with slight loss in accuracy
                using (Emgu.CV.Flann.LinearIndexParams ip = new Emgu.CV.Flann.LinearIndexParams()) 
                using (Emgu.CV.Flann.SearchParams sp = new SearchParams())
                using (DescriptorMatcher matcher = new FlannBasedMatcher(ip, sp))
                {
                    matcher.Add(modelDescriptors);

                    matcher.KnnMatch(observedDescriptors, matches, k, null);
                    mask = new Mat(matches.Size, 1, DepthType.Cv8U, 1);
                    mask.SetTo(new MCvScalar(255));
                    Features2DToolbox.VoteForUniqueness(matches, uniquenessThreshold, mask);


                    score = 0;
                    int nonZeroCount = CvInvoke.CountNonZero(mask);
                    if (nonZeroCount >= 4)
                    {
                        nonZeroCount = Features2DToolbox.VoteForSizeAndOrientation(modelKeyPoints, observedKeyPoints,
                            matches, mask, 1.5, 20);
                        if (nonZeroCount >= 4)
                        {
                            homography = Features2DToolbox.GetHomographyMatrixFromMatchedFeatures(modelKeyPoints,
                                observedKeyPoints, matches, mask, 2);

                            // Calculate score based on matches size
                            // ---------------------------------------------->
                            
                            for (int i = 0; i < matches.Size; i++)
                            {
                                //if (mask.GetData(i)[0] == 0) continue;
                                //foreach (var e in matches[i].ToArray())
                                //    ++score;
                            }
                            // <----------------------------------------------
                        }
                    }
                }
                watch.Stop();

            }
            matchTime = watch.ElapsedMilliseconds;
        }

        /// <summary>
        /// Draw the model image and observed image, the matched features and homography projection.
        /// </summary>
        /// <param name="modelImage">The model image</param>
        /// <param name="observedImages">The observed image</param>
        /// <param name="matchTime">The output total time for computing the homography matrix.</param>
        /// <returns>The model image and observed image, the matched features and homography projection.</returns>
        public static Rectangle Draw(Mat modelImage, Mat observedImages, out long matchTime)
        {
            List<Mat>humans = new List<Mat>();
            matchTime = 0;
            Image<Bgr, Byte> image = modelImage.ToImage<Bgr, Byte>();
            MCvObjectDetection[] rects;
            using (HOGDescriptor hog = new HOGDescriptor())
            {
                float[] desc = HOGDescriptor.GetDefaultPeopleDetector();
                hog.SetSVMDetector(desc);

                rects = hog.DetectMultiScale(image);

                foreach (MCvObjectDetection rect in rects)
                {
                    humans.Add(new Mat(image.Mat, rect.Rect));
                }
            }
            Mat result = new Mat();
            Rectangle human = new Rectangle();
            long pre_score = 0;
            VectorOfVectorOfDMatch pre_matches = new VectorOfVectorOfDMatch();
            for (int i = 0; i < humans.Count; i++)
            {
                Mat observedImage = humans[i];
                Mat homography;
                VectorOfKeyPoint modelKeyPoints;
                VectorOfKeyPoint observedKeyPoints;
                long score = 0;
                //VectorOfVectorOfDMatch matches = new VectorOfVectorOfDMatch();
                using (VectorOfVectorOfDMatch matches = new VectorOfVectorOfDMatch())
                {
                    Mat mask;
                    FindMatch(modelImage, observedImage, out matchTime, out modelKeyPoints, out observedKeyPoints, matches,
                       out mask, out homography, out score);

                    
                        //Draw the matched keypoints
                        Features2DToolbox.DrawMatches(modelImage, modelKeyPoints, observedImage, observedKeyPoints,
                           matches, result, new MCvScalar(255, 255, 255), new MCvScalar(255, 255, 255), mask);

                        #region draw the projected region on the image
                        if (homography != null)
                        {
                            //draw a rectangle along the projected model
                            Rectangle rect = new Rectangle(Point.Empty, modelImage.Size);
                            PointF[] pts = new PointF[]
                                {
                                new PointF(rect.Left, rect.Bottom),
                                new PointF(rect.Right, rect.Bottom),
                                new PointF(rect.Right, rect.Top),
                                new PointF(rect.Left, rect.Top)
                                };
                            pts = CvInvoke.PerspectiveTransform(pts, homography);

                            Point[] points = Array.ConvertAll<PointF, Point>(pts, Point.Round);
                            using (VectorOfPoint vp = new VectorOfPoint(points))
                            {
                                CvInvoke.Polylines(result, vp, true, new MCvScalar(255, 0, 0, 255), 5);
                            }
                         }
                        
                        #endregion 
                        if (pre_score < score)
                        {
                            pre_score = score;
                            human = rects[i].Rect;
                        }
                }
                
            }
            return human;

        }
    }
}