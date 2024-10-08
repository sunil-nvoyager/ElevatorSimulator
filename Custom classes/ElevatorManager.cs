﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;

namespace LiftSimulator
{
    /// <summary>    
    /// 1. Multithreading for elevators provided via ThreadPool class
    /// 2. Behaviour, when elevator was called:
    ///     - Use FindAllElevatorsWhichCanBeSent to pick elevators meeting any of following requirements:
    ///         - elevator is in its way to Passenger's floor (e.g. called by someone else)
    ///         - elevator is on different floor and its state is "Idle" 
    ///     - If list of available elevators is empty, do nothing
    /// 3. Manager has timer to periodcally (every 1000ms) check, if some floor doesn't need an elevator
    /// (which is signaled by Floor's LampUp and LampDown properties).
    /// </summary>
    public class ElevatorManager
    {
        #region FIELDS

        private readonly object locker = new object();
        private Elevator[] arrayOfAllElevators;
        public Elevator[] ArrayOfAllElevators
        {
            get;
            set;
        }

        private List<Elevator> listOfAllFreeElevators;

        private Floor[] arrayOfAllFloors;

        private System.Timers.Timer floorChecker;

        #endregion


        #region METHODS

        public ElevatorManager(Elevator[] ArrayOfAllElevators, Floor[] ArrayOfAllFloors)
        {
            
            //Initialize array with elevators
            this.arrayOfAllElevators = ArrayOfAllElevators;

            //Subscribe to elevators' events
            for (int i = 0; i < arrayOfAllElevators.Length; i++)
            {                
                arrayOfAllElevators[i].ElevatorIsFull += new EventHandler(ElevatorManager_ElevatorIsFull); //Subscribe to all ElevatorIsFull events
            }

            //Initialize array with floors
            this.arrayOfAllFloors = ArrayOfAllFloors;

            //Initialize list of free elevators
            this.listOfAllFreeElevators = new List<Elevator>();

            //Launch timer to periodically check, if some floor doesn't need an elevator
            this.floorChecker = new System.Timers.Timer(1000);
            this.floorChecker.Elapsed += new ElapsedEventHandler(this.ElevatorManager_TimerElapsed);
            this.floorChecker.Start();
            
        }

        /// <summary>
        /// Handles the request for an elevator by determining the appropriate action based on the passenger's floor and direction.
        /// </summary>
        /// <param name="PassengersFloor">The floor from which the passenger is requesting the elevator.</param>
        /// <param name="PassengersDirection">The direction in which the passenger intends to travel (up or down).</param>
        /// <remarks>
        /// This method is responsible for managing the elevator request process. It first locks the critical section to ensure thread safety, as it can be invoked from multiple threads, such as the ElevatorManager thread or its timer thread.
        /// Depending on the requested direction, it activates the corresponding lamp on the specified floor to indicate that an elevator is on its way.
        /// The method then searches for all available elevators that can respond to the request and selects the optimal one to send.
        /// If a suitable elevator is found, it proceeds to send that elevator to the passenger's floor.
        /// This ensures efficient handling of elevator requests and improves user experience by minimizing wait times.
        /// </remarks>
        public void PassengerNeedsAnElevator(Floor PassengersFloor, Direction PassengersDirection)
        {
            
            lock (locker)//Can be invoked from ElevatorManager thread or its timer thread
            {
                //Turn on appropriate lamp on the floor
                if (PassengersDirection == Direction.Up)
                {
                    PassengersFloor.LampUp = true;
                }
                else if (PassengersDirection == Direction.Down)
                {
                    PassengersFloor.LampDown = true;
                }

                //Search elevator
                FindAllElevatorsWhichCanBeSent(PassengersFloor, PassengersDirection);

                Elevator ElevatorToSend = ChooseOptimalElevatorToSend(PassengersFloor);

                if (ElevatorToSend != null)
                {
                    SendAnElevator(ElevatorToSend, PassengersFloor);
                }                
            }
            
        }

        /// <summary>
        /// Finds all elevators that can be sent to a specified floor in a given direction.
        /// </summary>
        /// <param name="PassengersFloor">The floor where the passengers are located.</param>
        /// <param name="PassengersDirection">The direction in which the passengers want to go.</param>
        /// <remarks>
        /// This method first clears the list of all free elevators. It then checks if there are any elevators already on their way to the passenger's floor by iterating through all available elevators and retrieving their list of floors to visit. 
        /// If an elevator is found that is already en route to the specified floor, the method clears the list of free elevators and exits early, as there is no need to send another elevator.
        /// If no elevators are found on their way, it proceeds to check for elevators that are currently idle (not moving). 
        /// Any idle elevators found are added to the list of free elevators, making them available for the passengers.
        /// </remarks>
        private void FindAllElevatorsWhichCanBeSent(Floor PassengersFloor, Direction PassengersDirection)
        {
            
            listOfAllFreeElevators.Clear();

            //Find elevators in their way to Passenger's floor (e.g. called by someone else)
            for (int i = 0; i < arrayOfAllElevators.Length; i++)
            {
                //Get list of floors to visit
                List<Floor> ListOfFloorsToVisit = arrayOfAllElevators[i].GetListOfAllFloorsToVisit();

                //Check list of floors to visit                
                if (ListOfFloorsToVisit.Contains(PassengersFloor))
                {
                    listOfAllFreeElevators.Clear();
                    return; //Some elevator is already in its way, no need to send new one
                }
            }

            //Find elevators, which are idling now (do not moving anywhere)
            for (int i = 0; i < arrayOfAllElevators.Length; i++)
            {
                if (arrayOfAllElevators[i].GetElevatorStatus() == ElevatorStatus.Idle) 
                {
                    listOfAllFreeElevators.Add(arrayOfAllElevators[i]);
                }
            }
            
        }

        /// <summary>
        /// Chooses the optimal elevator to send based on the floor where the call came from.
        /// </summary>
        /// <param name="FloorWhereTheCallCameFrom">The floor from which the elevator call was made.</param>
        /// <returns>The optimal elevator to send, or null if no free elevators are available.</returns>
        /// <remarks>
        /// This method checks the list of all available free elevators. If there are no free elevators in the list, it returns null, indicating that no elevator can be sent at this time.
        /// If there are free elevators, it selects and returns the first elevator from the list. 
        /// This approach assumes that the first elevator in the list is the most optimal choice for responding to the call.
        /// The method does not take into account the distance or current position of the elevators relative to the calling floor.
        /// </remarks>
        private Elevator ChooseOptimalElevatorToSend(Floor FloorWhereTheCallCameFrom)
        {
            
            //Check if listOfAllFreeElevators is not empty
            if (listOfAllFreeElevators.Count == 0)
            {
                return null;
            }

            //Return first elevator from the list
            return listOfAllFreeElevators[0];     
            
        }

        /// <summary>
        /// Sends an elevator to a specified target floor.
        /// </summary>
        /// <param name="ElevatorToSend">The elevator that will be sent to the target floor.</param>
        /// <param name="TargetFloor">The floor to which the elevator is being sent.</param>
        /// <remarks>
        /// This method adds the target floor to the list of floors that the specified elevator needs to visit.
        /// It then creates a new thread using the ThreadPool to prepare the elevator for its journey to the next floor in the list.
        /// This allows the elevator operation to run asynchronously, ensuring that the main thread remains responsive while the elevator is being dispatched.
        /// The method does not return any value and operates on the provided elevator instance directly.
        /// </remarks>
        private void SendAnElevator(Elevator ElevatorToSend, Floor TargetFloor)
        {            
            
            ElevatorToSend.AddNewFloorToTheList(TargetFloor);

            //Create new thread and send the elevator
            ThreadPool.QueueUserWorkItem(delegate { ElevatorToSend.PrepareElevatorToGoToNextFloorOnTheList(); });      
            
        }

        #endregion


        #region EVENT HANDLERS

        /// <summary>
        /// Handles the timer elapsed event for the elevator manager.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="ElapsedEventArgs"/> that contains the event data.</param>
        /// <remarks>
        /// This method checks each floor in the building to determine if there are passengers waiting for an elevator.
        /// It inspects the state of each floor's up and down lamps to identify if a passenger needs an elevator.
        /// If the up lamp is lit, it calls the <see cref="PassengerNeedsAnElevator"/> method with the direction set to Up.
        /// If the down lamp is lit, it calls the same method with the direction set to Down.
        /// To prevent sending multiple elevators simultaneously, there is a delay of 500 milliseconds after each request.
        /// This ensures that the elevator system operates smoothly and efficiently, responding to passenger needs without overwhelming the system.
        /// </remarks>
        public void ElevatorManager_TimerElapsed(object sender, ElapsedEventArgs e)
        {
            
            //Check if some floor doesn't need an elevator
            for (int i = 0; i < arrayOfAllFloors.Length; i++)
                {
                    if (arrayOfAllFloors[i].LampUp)
                    {
                        PassengerNeedsAnElevator(arrayOfAllFloors[i], Direction.Up);
                        Thread.Sleep(500); //delay to avoid sending two elevators at a time
                    }
                    else if(arrayOfAllFloors[i].LampDown)
                    {
                        PassengerNeedsAnElevator(arrayOfAllFloors[i], Direction.Down);
                        Thread.Sleep(500); //delay to avoid sending two elevators at a time
                    }   
                }
            
        }

        public void ElevatorManager_ElevatorIsFull(object sender, EventArgs e)
        {    
            //TO DO: Implement or remove!
        }
        
        #endregion EVENT HANDLERS
    }
}
