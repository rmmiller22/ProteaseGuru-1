﻿using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Tasks;

namespace ProteaseGuruGUI
{
    /// <summary>
    /// Interaction logic for ProteinResultsWindow.xaml
    /// </summary>
    public partial class ProteinResultsWindow : UserControl
    {
        //TODO error handling, click load proteins twice or click on summary

        private ObservableCollection<ProteinForTreeView> proteinTree;
        private ObservableCollection<ProteinForTreeView> filteredTree;
        private readonly Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;
        private Dictionary<Protein, Dictionary<string, List<InSilicoPep>>> PeptideByProteaseAndProtein;
        private Dictionary<Protein, ProteinForTreeView> ProteinsForTreeView;
        private Dictionary<InSilicoPep, (int,int)> partialPeptideMatches = new Dictionary<InSilicoPep, (int,int)>();
        private Dictionary<string, Color> ProteaseByColor;
        private List<Protein> ListOfProteinsOrderedByAccession;
        private Dictionary<string, ProteinForSeqCoverage> SequenceCoverageDisplays;
        private List<string> Proteases;

        public ProteinResultsWindow()
        {

        }

        public ProteinResultsWindow(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile) // change constructor to receive analysis information
        {
            InitializeComponent();
            PeptideByFile = peptideByFile;
            SequenceCoverageDisplays = new Dictionary<string, ProteinForSeqCoverage>();
            PeptideByProteaseAndProtein = new Dictionary<Protein, Dictionary<string, List<InSilicoPep>>>();
            proteinTree = new ObservableCollection<ProteinForTreeView>();
            filteredTree = new ObservableCollection<ProteinForTreeView>();
            ProteinsForTreeView = new Dictionary<Protein, ProteinForTreeView>();
            dataGridProteins.DataContext = proteinTree;

            SetUpDictionaries();

            this.Loaded += results_Loaded;

            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        public void SetUpDictionaries()
        {
            // TODO set colors for each available protease
            ProteaseByColor = new Dictionary<string, Color>();
            ProteaseByColor["trypsin"] = Colors.Blue;
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchModifications.SetTimer();
        }

        // handler for searching through tree
        private void searchBox_TextChangedHandler(object sender, EventArgs e)
        {
            string userInput = SearchTextBox.Text;

            if (string.IsNullOrEmpty(userInput))
            {
                dataGridProteins.DataContext = proteinTree;
                return;
            }

            searchProtein(userInput);
            dataGridProteins.DataContext = filteredTree;
            SearchModifications.Timer.Stop();
        }

        // search through protein list based on user input
        private void searchProtein(string txt)
        {
            filteredTree.Clear();
            foreach (var protein in proteinTree)
            {
                if (protein.DisplayName.ToUpper().Contains(txt.ToUpper()))
                {
                    filteredTree.Add(protein);
                }
            }
        }

        private void OnSelectionChanged()
        {
            // TODO disable selection of summary
            var protein = (ProteinForTreeView)dataGridProteins.SelectedItem;
            var protease = (string)ProteaseCoverageMaps.SelectedItem ?? Proteases.First(); // if not selected yet, display default

            // draw sequence coverage map
            DrawSequenceCoverageMap(protein, protease);

        }

        private void DrawSequenceCoverageMap(ProteinForTreeView protein, string protease) //string accession, Dictionary<string, PeptideForTreeView> uniquePeptides, Dictionary<string, PeptideForTreeView> sharedPeptides)
        {
            //string seqCoverage = SequenceCoverageDisplays[protein.Accession].Map;
            string seqCoverage = protein.Protein.BaseSequence;
            // mapViewer.Visibility = Visibility.Visible;
            map.Children.Clear();

            double spacing = 22;
            int height = 10;
            int totalHeight = 0;
            int accumIndex = 0;

            var splitSeq = Split(seqCoverage, spacing);
            var allPeptides = new List<InSilicoPep>(protein.AllPeptides);
            var indices = new Dictionary<int, List<int>>();

            // for debug
            //allPeptides = allPeptides.Where(p => p.BaseSequence.Contains("LFSTSR")).ToList();
            // allPeptides = allPeptides.Where(p => p.BaseSequence.Contains("KDKSK")).ToList();

            // draw sequence
            foreach (var line in splitSeq)
            {
                indices.Clear();
                for (int r = 0; r < line.Length; r++)
                {
                    SequenceCoverageMap.txtDrawing(map, new Point(r * spacing + 10, height), line[r].ToString().ToUpper(), Brushes.Black);
                }

                // highlight partial peptide sequences (broken off into multiple lines)
                if (partialPeptideMatches.Count > 0)
                {
                    var temp = new Dictionary<InSilicoPep, (int,int)>(partialPeptideMatches);
                    partialPeptideMatches.Clear();

                    foreach (var peptide in temp)
                    {
                        var remaining = peptide.Value.Item1;
                        var highlightIndex = peptide.Value.Item2;

                        int start = 0;
                        int end = Math.Min(remaining, line.Length - 1);

                        // continue highlighting peptide from previous line
                        SequenceCoverageMap.Highlight(start, end, map, indices, height, ProteaseByColor[protease], 
                            protein.UniquePeptides.Any(u => u.Equals(peptide.Key)), highlightIndex);
                    }
                }
                
                for (int i = 0; i < line.Length; ++i)
                {
                    // find peptides on this line
                    var temp = new List<InSilicoPep>(allPeptides.Where(p => p.StartResidue - accumIndex - 1 < line.Length).OrderBy(p => p.StartResidue));

                    foreach (InSilicoPep peptide in temp)
                    {
                        // identify partially highlighted peptides, will continue on next line
                        var partialIndex = CheckPartialMatch(peptide, line, accumIndex);

                        int start = peptide.StartResidue - accumIndex - 1;
                        int end = Math.Min(peptide.EndResidue - accumIndex - 1, line.Length - 1);
                        
                        var highlightIndex = SequenceCoverageMap.Highlight(start, end, map, indices, height, ProteaseByColor[protease], 
                            protein.UniquePeptides.Any(u => u.Equals(peptide)));

                        if (partialIndex >= 0)
                        {
                            partialPeptideMatches.Add(peptide, (partialIndex, highlightIndex));
                        }
                        allPeptides.Remove(peptide);
                    }
                }

                height += 100;
                accumIndex += line.Length;
            }

            totalHeight += splitSeq.Count() * 50;
            mapGrid.Height = totalHeight + 50;

            SequenceCoverageMap.drawLegend(legend, ProteaseByColor, protease, legendGrid);
        }

        private int CheckPartialMatch(InSilicoPep peptide, string line, int accumIndex)
        {
            int remaining = peptide.EndResidue - accumIndex - line.Length - 1;
            if (remaining >= 0)
            {
                return remaining;
            }

            return -1;
        }

        private List<string> Split(string sequence, double spacing)
        {
            int size = Convert.ToInt32(mapGrid.Width / spacing);
            var splitSequence = Enumerable.Range(0, sequence.Length / size).Select(i => sequence.Substring(i * size, size)).ToList();
            splitSequence.Add(sequence.Substring(splitSequence.Count() * size));

            return splitSequence;
        }
                
        public void CalculateSequenceCoverage()
        {
            foreach (var protein in ListOfProteinsOrderedByAccession)
            {
                var peptides = PeptideByProteaseAndProtein[protein].Values.SelectMany(p => p);
                HashSet<int> coveredOneBasedResidues = new HashSet<int>();

                // get residue numbers of each peptide in the protein and identify them as observed if the sequence is unambiguous
                foreach (var peptide in peptides)
                {
                    for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                    {
                        coveredOneBasedResidues.Add(i);
                    }
                }

                // convert the observed amino acids to upper case if they are unambiguously observed
                string seqCoverageMap = protein.BaseSequence.ToLower();
                var coverageArray = seqCoverageMap.ToCharArray();
                foreach (var obsResidueLocation in coveredOneBasedResidues)
                {
                    coverageArray[obsResidueLocation - 1] = char.ToUpper(coverageArray[obsResidueLocation - 1]);
                }
                seqCoverageMap = new string(coverageArray);

                double seqCoverageFract = (double)coveredOneBasedResidues.Count / protein.Length;

                // add the coverage display
                SequenceCoverageDisplays.Add(protein.Accession, new ProteinForSeqCoverage(protein.Accession, seqCoverageMap, seqCoverageFract));
            }
        }

        private void LoadProteins_Click(object sender, RoutedEventArgs e)
        {
            // populate proteins
            foreach (var db in PeptideByFile)
            {
                foreach (var protease in db.Value)
                {
                    // protease.Value is <Protein, List<Peptides>>
                    foreach (var protein in protease.Value)
                    {
                        var prot = protein.Key;
                        var peptidesByProtease = new Dictionary<string, List<InSilicoPep>>();

                        if (PeptideByProteaseAndProtein.ContainsKey(prot))
                        {
                            if (PeptideByProteaseAndProtein[prot].ContainsKey(protease.Key))
                            {
                                PeptideByProteaseAndProtein[prot][protease.Key].AddRange(protein.Value);
                            }
                            else
                            {
                                PeptideByProteaseAndProtein[prot].Add(protease.Key, protein.Value);
                            }
                        }
                        else
                        {
                            peptidesByProtease.Add(protease.Key, protein.Value);
                            PeptideByProteaseAndProtein.Add(prot, peptidesByProtease);

                            var name = prot.Name ?? prot.Accession;
                            var newPtv = new ProteinForTreeView(prot, name, new List<InSilicoPep>(), new List<InSilicoPep>(), new List<InSilicoPep>());
                            ProteinsForTreeView.Add(prot, newPtv);
                            proteinTree.Add(newPtv);
                        }

                        // define peptides for protein for tree view
                        ProteinsForTreeView[prot].AllPeptides.AddRange(protein.Value);
                        ProteinsForTreeView[prot].UniquePeptides.AddRange(protein.Value.Where(p => p.Unique));
                        ProteinsForTreeView[prot].SharedPeptides.AddRange(protein.Value.Where(p => !p.Unique));

                        // assign peptides
                        //foreach (var peptide in protein.Value)
                        //{
                        //    var ptv = ProteinsForTreeView[prot];

                        //    // add to all peptides
                        //    ptv.AllPeptides.Add(peptide.BaseSequence);
                        //    ptv.Children.Add(new PeptideForTreeView(peptide.BaseSequence, ptv));

                        //    if (peptide.Unique)
                        //    {
                        //        ptv.UniquePeptides.Add(peptide.BaseSequence);
                        //    }
                        //    else
                        //    {
                        //        ptv.SharedPeptides.Add(peptide.BaseSequence);
                        //    }
                        //}
                    }
                }
            }

            // add summary for proteins
            foreach (var protein in PeptideByProteaseAndProtein)
            {
                var ptv = ProteinsForTreeView[protein.Key];

                ptv.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + ptv.UniquePeptides.Count()));
                ptv.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + ptv.SharedPeptides.Count()));
                ptv.Summary.Add(new SummaryForTreeView("Sequence Coverage Fraction:")); // break down by protease

                foreach (var protease in protein.Value)
                {

                    ptv.Summary.Add(new SummaryForTreeView("Sequence Coverage: %")); // break down by protease
                }
            }


            ListOfProteinsOrderedByAccession = PeptideByProteaseAndProtein.Keys.OrderBy(p => p.Accession).ToList();
            CalculateSequenceCoverage();
        }

        private void ChangeMapScrollViewSize()
        {
            mapViewer.Height = 0.75 * ResultsGrid.ActualHeight;
            mapViewer.Width = 0.75 * ResultsGrid.ActualWidth;

            ChangeMapScrollViewVisibility();
        }

        private void ChangeMapScrollViewVisibility()
        {
            if (mapViewer.Width != 0 && mapGrid.Width > mapViewer.Width)
            {
                mapViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
            }
            else
            {
                mapViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            }


            if (mapViewer.Height != 0 && mapGrid.Height > mapViewer.Height)
            {
                mapViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            }
            else
            {
                mapViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            }
        }

        private void resultsSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ChangeMapScrollViewSize();
        }

        void results_Loaded(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            window.Closing += window_Closing;
        }

        void window_Closing(object sender, global::System.ComponentModel.CancelEventArgs e)
        {
            SearchModifications.Timer.Tick -= new EventHandler(searchBox_TextChangedHandler);
        }

        private void proteins_SelectedCellsChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // if protease selected item is null, dont draw map
            if (ProteaseCoverageMaps.SelectedItem != null)
            {
                OnSelectionChanged();
            }
        }

        private void proteaseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dataGridProteins.SelectedItem != null)
            {
                OnSelectionChanged();
            }
        }

        private void proteaseCoverageMaps_loaded(object sender, RoutedEventArgs e)
        {
            // get list of proteases to generate cov maps
            Proteases = PeptideByFile.SelectMany(p => p.Value.Keys).ToList();
            var combo = sender as ComboBox;
            combo.ItemsSource = Proteases;
        }
    }
}
