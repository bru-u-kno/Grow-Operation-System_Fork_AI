export interface NutrientProgramStageDto {
  stage: string
  dose: string
  target: string
  notes: string
}

export interface NutrientProgramDto {
  key: string
  name: string
  manufacturer: string
  category: string
  summary: string
  bestFor: string
  waterGuidance: string
  phGuidance: string
  ecGuidance: string
  stages: NutrientProgramStageDto[]
  tips: string[]
  feedChart?: FeedChartDto | null
}

export interface FeedChartItemDto {
  component: string
  minMlPerLiter: number
  maxMlPerLiter: number
}

export interface FeedChartColumnDto {
  id: string
  label: string
  stage: string
  week: number | null
  items: FeedChartItemDto[]
  ecTarget: number | null
  phMin: number | null
  phMax: number | null
}

export interface FeedChartDto {
  unit: string
  note: string | null
  columns: FeedChartColumnDto[]
}

export interface MediumPlaybookDto {
  key: string
  title: string
  summary: string
  focusPoints: string[]
  redFlags: string[]
}

export interface KnowledgeOverviewDto {
  programs: NutrientProgramDto[]
  playbooks: MediumPlaybookDto[]
}

export interface WearTemplateDto {
  schemaVersion: string
  id: string
  name: string
  category: string
  expectedLifespanDays: number
  replacementTriggers: string[]
  inspectionIntervalDays: number | null
}
