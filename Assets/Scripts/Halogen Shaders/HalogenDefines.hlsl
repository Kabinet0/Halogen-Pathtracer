#ifndef _HALOGEN_DEFINITIONS_INCLUDED
#define _HALOGEN_DEFINITIONS_INCLUDED

/* Constants */
#define QUESTIONABLE_IMPORTANCE_SAMPLING 1
#define QUESTIONABLE_IMPORTANCE_SAMPLING_RANGE 8.0f // deeply jank

/* Debugging settings for comparison */
#define OVERRIDE_SAMPLING_TO_PRNG 0
#define OVERRIDE_DISABLE_RUSSIAN_ROULETTE 0

static const float PI = radians(180);
static const float epsilon = 0.00001f;
static const float empty_medium_IOR = 1;
static const float FLOAT_2_PI = PI * 2;
  
#endif  